use crate::error::{AppError, Result};
use crate::paths::{profile_path, profiles_dir};
use serde::Serialize;
use std::path::{Path, PathBuf};

#[derive(Serialize, Clone)]
pub struct ProfileInfo {
    pub name: String,
    pub path: String,
    pub bepinex_version: Option<String>,
    pub mod_count: usize,
}

pub fn list_profiles(config_root: &Path) -> Result<Vec<ProfileInfo>> {
    let dir = profiles_dir(config_root);
    if !dir.is_dir() {
        return Ok(Vec::new());
    }
    let mut out = Vec::new();
    for entry in std::fs::read_dir(&dir)? {
        let entry = entry?;
        if !entry.file_type()?.is_dir() {
            continue;
        }
        let name = entry.file_name().to_string_lossy().to_string();
        let path = entry.path();
        let version = read_profile_version(&path);
        let mod_count = count_mods(&path);
        out.push(ProfileInfo {
            name,
            path: path.to_string_lossy().to_string(),
            bepinex_version: version,
            mod_count,
        });
    }
    out.sort_by(|a, b| a.name.cmp(&b.name));
    Ok(out)
}

fn read_profile_version(profile: &Path) -> Option<String> {
    let marker = profile.join("bepinex_version.txt");
    if marker.is_file() {
        return std::fs::read_to_string(marker).ok().map(|s| s.trim().to_string());
    }
    let core = profile
        .join("BepInEx")
        .join("core")
        .join("BepInEx.Unity.IL2CPP.dll");
    if core.is_file() {
        Some("installed".into())
    } else {
        None
    }
}

fn count_mods(profile: &Path) -> usize {
    let plugins = profile.join("BepInEx").join("plugins");
    if !plugins.is_dir() {
        return 0;
    }
    std::fs::read_dir(plugins)
        .map(|rd| {
            rd.filter_map(|e| e.ok())
                .filter(|e| e.file_type().map(|t| t.is_dir()).unwrap_or(false))
                .count()
        })
        .unwrap_or(0)
}

pub fn validate_profile_name(name: &str) -> Result<()> {
    let name = name.trim();
    if name.is_empty() {
        return Err(AppError::msg("配置名不能为空"));
    }
    if name.contains(['/', '\\', ':', '*', '?', '"', '<', '>', '|']) {
        return Err(AppError::msg("配置名含有非法字符"));
    }
    if name == "." || name == ".." {
        return Err(AppError::msg("配置名非法"));
    }
    Ok(())
}

pub fn unique_profile_name(config_root: &Path, base: &str) -> String {
    let base = base.trim();
    let mut candidate = base.to_string();
    let mut n = 2;
    while profile_path(config_root, &candidate).exists() {
        candidate = format!("{base} ({n})");
        n += 1;
    }
    candidate
}

pub fn rename_profile(config_root: &Path, old: &str, new: &str) -> Result<String> {
    validate_profile_name(new)?;
    let from = profile_path(config_root, old);
    let to = profile_path(config_root, new.trim());
    if !from.is_dir() {
        return Err(AppError::msg("配置不存在"));
    }
    if to.exists() {
        return Err(AppError::msg("目标配置名已存在"));
    }
    std::fs::rename(&from, &to)?;
    Ok(new.trim().to_string())
}

pub fn delete_profile(config_root: &Path, name: &str) -> Result<()> {
    let path = profile_path(config_root, name);
    if !path.is_dir() {
        return Err(AppError::msg("配置不存在"));
    }
    std::fs::remove_dir_all(&path)?;
    Ok(())
}

pub fn ensure_profile_layout(dest: &Path) -> Result<()> {
    std::fs::create_dir_all(dest.join("BepInEx").join("plugins"))?;
    std::fs::create_dir_all(dest.join("BepInEx").join("config"))?;
    std::fs::create_dir_all(dest.join("BepInEx").join("patchers"))?;
    std::fs::create_dir_all(dest.join("BepInEx").join("core"))?;
    Ok(())
}

pub fn profile_plugins(profile: &Path) -> PathBuf {
    profile.join("BepInEx").join("plugins")
}
