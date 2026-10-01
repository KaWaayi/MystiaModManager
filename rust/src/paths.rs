use crate::error::{AppError, Result};
use serde::Serialize;
use std::path::{Path, PathBuf};
use winreg::enums::*;
use winreg::RegKey;

pub const STEAM_APP_ID: &str = "1584090";
pub const GAME_FOLDER: &str = "Touhou Mystia Izakaya";
pub const GAME_EXE: &str = "Touhou Mystia Izakaya.exe";
pub const GAME_PROCESS: &str = "Touhou Mystia Izakaya";
pub const MANAGER_DIR_NAME: &str = "MystiaModManager";
pub const CONFIG_DIR_NAME: &str = "MystiaModManagerConfig";

#[derive(Serialize, Clone)]
pub struct DefaultPaths {
    pub game_path: String,
    pub manager_path: String,
    pub config_root: String,
}

pub fn steam_install_path() -> Option<PathBuf> {
    let hkcu = RegKey::predef(HKEY_CURRENT_USER);
    if let Ok(key) = hkcu.open_subkey("Software\\Valve\\Steam") {
        if let Ok(path) = key.get_value::<String, _>("SteamPath") {
            let p = PathBuf::from(path.replace('/', "\\"));
            if p.exists() {
                return Some(p);
            }
        }
    }
    let hklm = RegKey::predef(HKEY_LOCAL_MACHINE);
    if let Ok(key) = hklm.open_subkey("SOFTWARE\\WOW6432Node\\Valve\\Steam") {
        if let Ok(path) = key.get_value::<String, _>("InstallPath") {
            let p = PathBuf::from(path);
            if p.exists() {
                return Some(p);
            }
        }
    }
    None
}

fn parse_library_folders(vdf: &str) -> Vec<PathBuf> {
    let mut paths = Vec::new();
    for line in vdf.lines() {
        let t = line.trim();
        if let Some(rest) = t.strip_prefix("\"path\"") {
            let rest = rest.trim();
            if let Some(start) = rest.find('"') {
                let s = &rest[start + 1..];
                if let Some(end) = s.find('"') {
                    let path = s[..end].replace("\\\\", "\\");
                    paths.push(PathBuf::from(path));
                }
            }
        }
    }
    paths
}

pub fn detect_game_path() -> Option<PathBuf> {
    let steam = steam_install_path()?;
    let vdf_path = steam.join("steamapps").join("libraryfolders.vdf");
    let content = std::fs::read_to_string(&vdf_path).ok()?;
    let libraries = parse_library_folders(&content);
    let mut libs = libraries;
    if libs.is_empty() {
        libs.push(steam);
    }
    for lib in libs {
        let candidate = lib
            .join("steamapps")
            .join("common")
            .join(GAME_FOLDER);
        let exe = candidate.join(GAME_EXE);
        if exe.is_file() {
            return Some(candidate);
        }
        // Also check if app id is listed then still try the path
        let manifest = lib
            .join("steamapps")
            .join(format!("appmanifest_{STEAM_APP_ID}.acf"));
        if manifest.is_file() {
            let c = lib
                .join("steamapps")
                .join("common")
                .join(GAME_FOLDER);
            if c.join(GAME_EXE).is_file() {
                return Some(c);
            }
        }
    }
    None
}

pub fn default_paths_for_game(game_path: &Path) -> Result<DefaultPaths> {
    if !game_path.join(GAME_EXE).is_file() {
        return Err(AppError::msg(format!(
            "游戏目录无效，找不到 {}",
            GAME_EXE
        )));
    }
    let drive = game_path
        .components()
        .next()
        .and_then(|c| match c {
            std::path::Component::Prefix(p) => Some(p.as_os_str().to_string_lossy().to_string()),
            _ => None,
        })
        .unwrap_or_else(|| "C:".into());

    let is_c = drive.eq_ignore_ascii_case("C:") || drive.eq_ignore_ascii_case("C:\\");
    let (manager_path, config_root) = if is_c {
        let appdata = std::env::var("APPDATA").map_err(|_| AppError::msg("找不到 APPDATA"))?;
        (
            PathBuf::from(&appdata).join(MANAGER_DIR_NAME),
            PathBuf::from(&appdata).join(CONFIG_DIR_NAME),
        )
    } else {
        let root = PathBuf::from(format!("{drive}\\"));
        (root.join(MANAGER_DIR_NAME), root.join(CONFIG_DIR_NAME))
    };

    Ok(DefaultPaths {
        game_path: game_path.to_string_lossy().to_string(),
        manager_path: manager_path.to_string_lossy().to_string(),
        config_root: config_root.to_string_lossy().to_string(),
    })
}

pub fn detect_default_paths() -> Result<DefaultPaths> {
    let game = detect_game_path().ok_or_else(|| AppError::msg("未检测到夜雀食堂安装路径"))?;
    default_paths_for_game(&game)
}

pub fn profiles_dir(config_root: &Path) -> PathBuf {
    config_root.join("profiles")
}

pub fn profile_path(config_root: &Path, name: &str) -> PathBuf {
    profiles_dir(config_root).join(name)
}

pub fn manager_json_path(config_root: &Path) -> PathBuf {
    config_root.join("manager.json")
}

pub fn ensure_same_path_rejected(manager: &Path, config: &Path) -> Result<()> {
    let m = dunce_canonicalize(manager)?;
    let c = dunce_canonicalize(config).unwrap_or_else(|_| config.to_path_buf());
    if m == c {
        return Err(AppError::msg("管理器目录与配置目录不能相同"));
    }
    Ok(())
}

fn dunce_canonicalize(p: &Path) -> Result<PathBuf> {
    if p.exists() {
        Ok(std::fs::canonicalize(p)?)
    } else {
        Ok(p.to_path_buf())
    }
}
