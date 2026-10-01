use crate::error::{AppError, Result};
use crate::paths::GAME_EXE;
use serde::Serialize;
use std::path::{Path, PathBuf};

const DOORSTOP_INI_RELATIVE: &str = r#"# General options for Unity Doorstop
[General]

# Enable Doorstop?
enabled = true

# Path to the assembly to load and execute
# NOTE: The entrypoint must be of format `static void Doorstop.Entrypoint.Start()`
target_assembly = BepInEx\core\BepInEx.Unity.IL2CPP.dll

# If true, Unity's output log is redirected to <current folder>\output_log.txt
redirect_output_log = false

# Overrides the default boot.config file path
boot_config_override =

# If enabled, DOORSTOP_DISABLE env var value is ignored
ignore_disable_switch = false

# Options specific to running under Unity Mono runtime
[UnityMono]

dll_search_path_override =
debug_enabled = false
debug_start_server = true
debug_address = 127.0.0.1:10000
debug_suspend = false

# Options specific to running under Il2Cpp runtime
[Il2Cpp]

# Path to coreclr.dll that contains the CoreCLR runtime
coreclr_path = dotnet\coreclr.dll

# Path to the directory containing the managed core libraries for CoreCLR
corlib_dir = dotnet
"#;

#[derive(Serialize)]
pub struct LaunchInfo {
    pub exe_path: String,
    pub working_directory: String,
    pub arguments: Vec<String>,
}

pub fn write_game_doorstop_hook(game_path: &Path, profile: &Path) -> Result<()> {
    if !game_path.join(GAME_EXE).is_file() {
        return Err(AppError::msg("游戏目录无效"));
    }
    let winhttp_src = profile.join("winhttp.dll");
    if !winhttp_src.is_file() {
        return Err(AppError::msg("配置中缺少 winhttp.dll，请先安装 BepInEx"));
    }
    std::fs::copy(&winhttp_src, game_path.join("winhttp.dll"))?;
    std::fs::write(game_path.join("doorstop_config.ini"), DOORSTOP_INI_RELATIVE)?;

    let ver_src = profile.join(".doorstop_version");
    if ver_src.is_file() {
        std::fs::copy(&ver_src, game_path.join(".doorstop_version"))?;
    } else {
        std::fs::write(game_path.join(".doorstop_version"), "4.4.0")?;
    }
    Ok(())
}

pub fn prepare_launch(game_path: &Path, profile: &Path) -> Result<LaunchInfo> {
    write_game_doorstop_hook(game_path, profile)?;

    let target = profile
        .join("BepInEx")
        .join("core")
        .join("BepInEx.Unity.IL2CPP.dll");
    if !target.is_file() {
        return Err(AppError::msg("配置中缺少 BepInEx.Unity.IL2CPP.dll"));
    }
    let coreclr = profile.join("dotnet").join("coreclr.dll");
    if !coreclr.is_file() {
        return Err(AppError::msg("配置中缺少 dotnet\\coreclr.dll"));
    }
    let corlib = profile.join("dotnet");

    let target_s = abs_str(&target)?;
    let coreclr_s = abs_str(&coreclr)?;
    let corlib_s = abs_str(&corlib)?;

    let args = vec![
        "--doorstop-enabled".into(),
        "true".into(),
        "--doorstop-target-assembly".into(),
        target_s,
        "--doorstop-clr-runtime-coreclr-path".into(),
        coreclr_s,
        "--doorstop-clr-corlib-dir".into(),
        corlib_s,
    ];

    Ok(LaunchInfo {
        exe_path: game_path.join(GAME_EXE).to_string_lossy().to_string(),
        working_directory: game_path.to_string_lossy().to_string(),
        arguments: args,
    })
}

fn abs_str(p: &Path) -> Result<String> {
    let c = if p.exists() {
        std::fs::canonicalize(p)?
    } else {
        p.to_path_buf()
    };
    Ok(strip_unc(c).to_string_lossy().to_string())
}

fn strip_unc(p: PathBuf) -> PathBuf {
    let s = p.to_string_lossy();
    if let Some(rest) = s.strip_prefix(r"\\?\") {
        PathBuf::from(rest)
    } else {
        p
    }
}
