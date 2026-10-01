use crate::error::ApiResult;
use std::ffi::{CStr, CString};
use std::os::raw::c_char;
use std::path::Path;

fn cstr_to_str<'a>(p: *const c_char) -> Result<&'a str, String> {
    if p.is_null() {
        return Err("空指针".into());
    }
    unsafe { CStr::from_ptr(p) }
        .to_str()
        .map_err(|e| e.to_string())
}

fn to_c_string_json<T: serde::Serialize>(value: &T) -> *mut c_char {
    match serde_json::to_string(value) {
        Ok(s) => CString::new(s)
            .map(|c| c.into_raw())
            .unwrap_or_else(|_| CString::new("{\"ok\":false,\"error\":\"nul in json\"}").unwrap().into_raw()),
        Err(e) => {
            let err = ApiResult::<()>::err(e);
            let s = serde_json::to_string(&err).unwrap_or_else(|_| "{\"ok\":false}".into());
            CString::new(s).unwrap().into_raw()
        }
    }
}

fn ok_json<T: serde::Serialize>(data: T) -> *mut c_char {
    to_c_string_json(&ApiResult::ok(data))
}

fn err_json(e: impl ToString) -> *mut c_char {
    to_c_string_json(&ApiResult::<()>::err(e))
}

#[no_mangle]
pub extern "C" fn mystia_free_string(s: *mut c_char) {
    if s.is_null() {
        return;
    }
    unsafe {
        drop(CString::from_raw(s));
    }
}

#[no_mangle]
pub extern "C" fn mystia_detect_default_paths() -> *mut c_char {
    match crate::paths::detect_default_paths() {
        Ok(p) => ok_json(p),
        Err(e) => err_json(e),
    }
}

#[no_mangle]
pub extern "C" fn mystia_default_paths_for_game(game_path: *const c_char) -> *mut c_char {
    let game = match cstr_to_str(game_path) {
        Ok(s) => s,
        Err(e) => return err_json(e),
    };
    match crate::paths::default_paths_for_game(Path::new(game)) {
        Ok(p) => ok_json(p),
        Err(e) => err_json(e),
    }
}

#[no_mangle]
pub extern "C" fn mystia_init_manager(
    game_path: *const c_char,
    manager_path: *const c_char,
    config_root: *const c_char,
) -> *mut c_char {
    let (game, manager, config) = match (
        cstr_to_str(game_path),
        cstr_to_str(manager_path),
        cstr_to_str(config_root),
    ) {
        (Ok(a), Ok(b), Ok(c)) => (a, b, c),
        (Err(e), _, _) | (_, Err(e), _) | (_, _, Err(e)) => return err_json(e),
    };
    if let Err(e) = crate::paths::ensure_same_path_rejected(Path::new(manager), Path::new(config)) {
        return err_json(e);
    }
    match crate::manager::init_or_load(game, manager, config) {
        Ok(s) => ok_json(s),
        Err(e) => err_json(e),
    }
}

#[no_mangle]
pub extern "C" fn mystia_load_manager(config_root: *const c_char) -> *mut c_char {
    let config = match cstr_to_str(config_root) {
        Ok(s) => s,
        Err(e) => return err_json(e),
    };
    match crate::manager::ManagerSettings::load(Path::new(config)) {
        Ok(s) => ok_json(s),
        Err(e) => err_json(e),
    }
}

#[no_mangle]
pub extern "C" fn mystia_save_manager(config_root: *const c_char, json: *const c_char) -> *mut c_char {
    let (config, json_s) = match (cstr_to_str(config_root), cstr_to_str(json)) {
        (Ok(a), Ok(b)) => (a, b),
        (Err(e), _) | (_, Err(e)) => return err_json(e),
    };
    let settings: crate::manager::ManagerSettings = match serde_json::from_str(json_s) {
        Ok(s) => s,
        Err(e) => return err_json(e),
    };
    match settings.save(Path::new(config)) {
        Ok(()) => ok_json(true),
        Err(e) => err_json(e),
    }
}

#[no_mangle]
pub extern "C" fn mystia_list_profiles(config_root: *const c_char) -> *mut c_char {
    let config = match cstr_to_str(config_root) {
        Ok(s) => s,
        Err(e) => return err_json(e),
    };
    match crate::profiles::list_profiles(Path::new(config)) {
        Ok(v) => ok_json(v),
        Err(e) => err_json(e),
    }
}

#[no_mangle]
pub extern "C" fn mystia_list_bepinex_builds() -> *mut c_char {
    match crate::bepinex::list_il2cpp_win_x64_builds() {
        Ok(v) => ok_json(v),
        Err(e) => err_json(e),
    }
}

#[no_mangle]
pub extern "C" fn mystia_create_profile(
    config_root: *const c_char,
    name: *const c_char,
    download_url: *const c_char,
    version_label: *const c_char,
) -> *mut c_char {
    if let Err(e) = crate::process::ensure_game_not_running() {
        return err_json(e);
    }
    let (config, name, url, ver) = match (
        cstr_to_str(config_root),
        cstr_to_str(name),
        cstr_to_str(download_url),
        cstr_to_str(version_label),
    ) {
        (Ok(a), Ok(b), Ok(c), Ok(d)) => (a, b, c, d),
        (Err(e), _, _, _) | (_, Err(e), _, _) | (_, _, Err(e), _) | (_, _, _, Err(e)) => {
            return err_json(e)
        }
    };
    match crate::bepinex::create_profile(Path::new(config), name, url, ver) {
        Ok(n) => ok_json(n),
        Err(e) => err_json(e),
    }
}

#[no_mangle]
pub extern "C" fn mystia_update_bepinex(
    profile_path: *const c_char,
    download_url: *const c_char,
    version_label: *const c_char,
) -> *mut c_char {
    if let Err(e) = crate::process::ensure_game_not_running() {
        return err_json(e);
    }
    let (profile, url, ver) = match (
        cstr_to_str(profile_path),
        cstr_to_str(download_url),
        cstr_to_str(version_label),
    ) {
        (Ok(a), Ok(b), Ok(c)) => (a, b, c),
        (Err(e), _, _) | (_, Err(e), _) | (_, _, Err(e)) => return err_json(e),
    };
    match crate::bepinex::update_profile_framework(Path::new(profile), url, ver) {
        Ok(()) => ok_json(true),
        Err(e) => err_json(e),
    }
}

#[no_mangle]
pub extern "C" fn mystia_rename_profile(
    config_root: *const c_char,
    old_name: *const c_char,
    new_name: *const c_char,
) -> *mut c_char {
    if let Err(e) = crate::process::ensure_game_not_running() {
        return err_json(e);
    }
    let (config, old, new) = match (
        cstr_to_str(config_root),
        cstr_to_str(old_name),
        cstr_to_str(new_name),
    ) {
        (Ok(a), Ok(b), Ok(c)) => (a, b, c),
        (Err(e), _, _) | (_, Err(e), _) | (_, _, Err(e)) => return err_json(e),
    };
    match crate::profiles::rename_profile(Path::new(config), old, new) {
        Ok(n) => ok_json(n),
        Err(e) => err_json(e),
    }
}

#[no_mangle]
pub extern "C" fn mystia_delete_profile(
    config_root: *const c_char,
    name: *const c_char,
) -> *mut c_char {
    if let Err(e) = crate::process::ensure_game_not_running() {
        return err_json(e);
    }
    let (config, name) = match (cstr_to_str(config_root), cstr_to_str(name)) {
        (Ok(a), Ok(b)) => (a, b),
        (Err(e), _) | (_, Err(e)) => return err_json(e),
    };
    match crate::profiles::delete_profile(Path::new(config), name) {
        Ok(()) => ok_json(true),
        Err(e) => err_json(e),
    }
}

#[no_mangle]
pub extern "C" fn mystia_list_mods(profile_path: *const c_char) -> *mut c_char {
    let profile = match cstr_to_str(profile_path) {
        Ok(s) => s,
        Err(e) => return err_json(e),
    };
    match crate::mods::list_mods(Path::new(profile)) {
        Ok(v) => ok_json(v),
        Err(e) => err_json(e),
    }
}

#[no_mangle]
pub extern "C" fn mystia_install_mod(
    profile_path: *const c_char,
    source_path: *const c_char,
) -> *mut c_char {
    if let Err(e) = crate::process::ensure_game_not_running() {
        return err_json(e);
    }
    let (profile, source) = match (cstr_to_str(profile_path), cstr_to_str(source_path)) {
        (Ok(a), Ok(b)) => (a, b),
        (Err(e), _) | (_, Err(e)) => return err_json(e),
    };
    match crate::mods::install_mod_from_path(Path::new(profile), Path::new(source)) {
        Ok(n) => ok_json(n),
        Err(e) => err_json(e),
    }
}

#[no_mangle]
pub extern "C" fn mystia_set_mod_enabled(
    profile_path: *const c_char,
    mod_name: *const c_char,
    enabled: bool,
) -> *mut c_char {
    if let Err(e) = crate::process::ensure_game_not_running() {
        return err_json(e);
    }
    let (profile, name) = match (cstr_to_str(profile_path), cstr_to_str(mod_name)) {
        (Ok(a), Ok(b)) => (a, b),
        (Err(e), _) | (_, Err(e)) => return err_json(e),
    };
    match crate::mods::set_mod_enabled(Path::new(profile), name, enabled) {
        Ok(()) => ok_json(true),
        Err(e) => err_json(e),
    }
}

#[no_mangle]
pub extern "C" fn mystia_uninstall_mod(
    profile_path: *const c_char,
    mod_name: *const c_char,
) -> *mut c_char {
    if let Err(e) = crate::process::ensure_game_not_running() {
        return err_json(e);
    }
    let (profile, name) = match (cstr_to_str(profile_path), cstr_to_str(mod_name)) {
        (Ok(a), Ok(b)) => (a, b),
        (Err(e), _) | (_, Err(e)) => return err_json(e),
    };
    match crate::mods::uninstall_mod(Path::new(profile), name) {
        Ok(()) => ok_json(true),
        Err(e) => err_json(e),
    }
}

#[no_mangle]
pub extern "C" fn mystia_write_doorstop_hook(
    game_path: *const c_char,
    profile_path: *const c_char,
) -> *mut c_char {
    if let Err(e) = crate::process::ensure_game_not_running() {
        return err_json(e);
    }
    let (game, profile) = match (cstr_to_str(game_path), cstr_to_str(profile_path)) {
        (Ok(a), Ok(b)) => (a, b),
        (Err(e), _) | (_, Err(e)) => return err_json(e),
    };
    match crate::doorstop::write_game_doorstop_hook(Path::new(game), Path::new(profile)) {
        Ok(()) => ok_json(true),
        Err(e) => err_json(e),
    }
}

#[no_mangle]
pub extern "C" fn mystia_prepare_launch(
    game_path: *const c_char,
    profile_path: *const c_char,
) -> *mut c_char {
    if let Err(e) = crate::process::ensure_game_not_running() {
        return err_json(e);
    }
    let (game, profile) = match (cstr_to_str(game_path), cstr_to_str(profile_path)) {
        (Ok(a), Ok(b)) => (a, b),
        (Err(e), _) | (_, Err(e)) => return err_json(e),
    };
    match crate::doorstop::prepare_launch(Path::new(game), Path::new(profile)) {
        Ok(info) => ok_json(info),
        Err(e) => err_json(e),
    }
}

#[no_mangle]
pub extern "C" fn mystia_is_game_running() -> bool {
    crate::process::is_game_running()
}

#[no_mangle]
pub extern "C" fn mystia_list_cfg_files(profile_path: *const c_char) -> *mut c_char {
    let profile = match cstr_to_str(profile_path) {
        Ok(s) => s,
        Err(e) => return err_json(e),
    };
    match crate::cfg::list_cfg_files(Path::new(profile)) {
        Ok(v) => ok_json(v),
        Err(e) => err_json(e),
    }
}

#[no_mangle]
pub extern "C" fn mystia_load_cfg(profile_path: *const c_char, file_name: *const c_char) -> *mut c_char {
    let (profile, file) = match (cstr_to_str(profile_path), cstr_to_str(file_name)) {
        (Ok(a), Ok(b)) => (a, b),
        (Err(e), _) | (_, Err(e)) => return err_json(e),
    };
    match crate::cfg::load_cfg(Path::new(profile), file) {
        Ok(v) => ok_json(v),
        Err(e) => err_json(e),
    }
}

#[no_mangle]
pub extern "C" fn mystia_save_cfg(
    profile_path: *const c_char,
    file_name: *const c_char,
    updates_json: *const c_char,
) -> *mut c_char {
    if let Err(e) = crate::process::ensure_game_not_running() {
        return err_json(e);
    }
    let (profile, file, json) = match (
        cstr_to_str(profile_path),
        cstr_to_str(file_name),
        cstr_to_str(updates_json),
    ) {
        (Ok(a), Ok(b), Ok(c)) => (a, b, c),
        (Err(e), _, _) | (_, Err(e), _) | (_, _, Err(e)) => return err_json(e),
    };
    let updates: Vec<crate::cfg::CfgUpdate> = match serde_json::from_str(json) {
        Ok(v) => v,
        Err(e) => return err_json(e),
    };
    match crate::cfg::save_cfg(Path::new(profile), file, &updates) {
        Ok(()) => ok_json(true),
        Err(e) => err_json(e),
    }
}

#[no_mangle]
pub extern "C" fn mystia_profile_path(
    config_root: *const c_char,
    name: *const c_char,
) -> *mut c_char {
    let (config, name) = match (cstr_to_str(config_root), cstr_to_str(name)) {
        (Ok(a), Ok(b)) => (a, b),
        (Err(e), _) | (_, Err(e)) => return err_json(e),
    };
    let p = crate::paths::profile_path(Path::new(config), name);
    ok_json(p.to_string_lossy().to_string())
}
