use crate::paths::GAME_PROCESS;

#[cfg(windows)]
pub fn is_game_running() -> bool {
    use windows_sys::Win32::Foundation::CloseHandle;
    use windows_sys::Win32::System::ProcessStatus::{
        K32EnumProcesses, K32GetModuleBaseNameW,
    };
    use windows_sys::Win32::System::Threading::{OpenProcess, PROCESS_QUERY_INFORMATION, PROCESS_VM_READ};

    unsafe {
        let mut pids = [0u32; 4096];
        let mut bytes_returned = 0u32;
        if K32EnumProcesses(
            pids.as_mut_ptr(),
            (pids.len() * std::mem::size_of::<u32>()) as u32,
            &mut bytes_returned,
        ) == 0
        {
            return false;
        }
        let count = bytes_returned as usize / std::mem::size_of::<u32>();
        let target = GAME_PROCESS.to_lowercase();
        for &pid in &pids[..count] {
            if pid == 0 {
                continue;
            }
            let handle = OpenProcess(PROCESS_QUERY_INFORMATION | PROCESS_VM_READ, 0, pid);
            if handle.is_null() {
                continue;
            }
            let mut name = [0u16; 260];
            let len = K32GetModuleBaseNameW(handle, std::ptr::null_mut(), name.as_mut_ptr(), name.len() as u32);
            CloseHandle(handle);
            if len == 0 {
                continue;
            }
            let process_name = String::from_utf16_lossy(&name[..len as usize]).to_lowercase();
            // Compare without .exe
            let base = process_name.trim_end_matches(".exe");
            if base == target {
                return true;
            }
        }
        false
    }
}

#[cfg(not(windows))]
pub fn is_game_running() -> bool {
    false
}

pub fn ensure_game_not_running() -> crate::error::Result<()> {
    if is_game_running() {
        return Err(crate::error::AppError::msg(
            "游戏正在运行，请先退出游戏后再操作",
        ));
    }
    Ok(())
}
