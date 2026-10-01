use crate::error::{AppError, Result};
use crate::paths::profiles_dir;
use crate::profiles::{ensure_profile_layout, unique_profile_name, validate_profile_name};
use regex::Regex;
use serde::Serialize;
use std::fs::File;
use std::io::copy;
use std::path::Path;
use zip::ZipArchive;

const BUILDS_PAGE: &str = "https://builds.bepinex.dev/projects/bepinex_be";
const BUILDS_HOST: &str = "https://builds.bepinex.dev";

#[derive(Serialize, Clone)]
pub struct BepInExBuild {
    pub build_id: u32,
    pub version: String,
    pub file_name: String,
    pub url: String,
}

pub fn list_il2cpp_win_x64_builds() -> Result<Vec<BepInExBuild>> {
    let body = http_get_text(BUILDS_PAGE)?;
    let re = Regex::new(
        r#"/projects/bepinex_be/(\d+)/(BepInEx-Unity\.IL2CPP-win-x64-6[^"\s>]+\.zip)"#,
    )
    .map_err(|e| AppError::msg(e.to_string()))?;

    let mut builds = Vec::new();
    let mut seen = std::collections::HashSet::new();
    for cap in re.captures_iter(&body) {
        let build_id: u32 = cap[1].parse().unwrap_or(0);
        let raw_name = &cap[2];
        let file_name = raw_name.replace("%2B", "+");
        let key = format!("{build_id}:{file_name}");
        if !seen.insert(key) {
            continue;
        }
        let version = file_name
            .trim_start_matches("BepInEx-Unity.IL2CPP-win-x64-")
            .trim_end_matches(".zip")
            .to_string();
        let url = format!(
            "{BUILDS_HOST}/projects/bepinex_be/{build_id}/{}",
            raw_name
        );
        builds.push(BepInExBuild {
            build_id,
            version,
            file_name,
            url,
        });
    }
    builds.sort_by(|a, b| b.build_id.cmp(&a.build_id));
    if builds.is_empty() {
        return Err(AppError::msg("构建站未找到 IL2CPP win-x64 6.x 包"));
    }
    Ok(builds)
}

fn http_get_text(url: &str) -> Result<String> {
    let resp = ureq::get(url)
        .set("User-Agent", "MystiaModManager/0.1")
        .call()
        .map_err(|e| AppError::Http(e.to_string()))?;
    resp.into_string()
        .map_err(|e| AppError::Http(e.to_string()))
}

fn download_to_file(url: &str, dest: &Path) -> Result<()> {
    let resp = ureq::get(url)
        .set("User-Agent", "MystiaModManager/0.1")
        .call()
        .map_err(|e| AppError::Http(e.to_string()))?;
    let mut reader = resp.into_reader();
    if let Some(parent) = dest.parent() {
        std::fs::create_dir_all(parent)?;
    }
    let mut file = File::create(dest)?;
    copy(&mut reader, &mut file)?;
    Ok(())
}

fn extract_zip(zip_path: &Path, dest: &Path) -> Result<()> {
    let file = File::open(zip_path)?;
    let mut archive = ZipArchive::new(file)?;
    for i in 0..archive.len() {
        let mut file = archive.by_index(i)?;
        let outpath = match file.enclosed_name() {
            Some(p) => dest.join(p),
            None => continue,
        };
        if file.name().ends_with('/') {
            std::fs::create_dir_all(&outpath)?;
        } else {
            if let Some(parent) = outpath.parent() {
                std::fs::create_dir_all(parent)?;
            }
            let mut outfile = File::create(&outpath)?;
            copy(&mut file, &mut outfile)?;
        }
    }
    Ok(())
}

/// Create a new profile by downloading and extracting a BepInEx package.
pub fn create_profile(
    config_root: &Path,
    name: &str,
    download_url: &str,
    version_label: &str,
) -> Result<String> {
    validate_profile_name(name)?;
    let final_name = if profile_exists(config_root, name) {
        unique_profile_name(config_root, name)
    } else {
        name.trim().to_string()
    };
    let dest = crate::paths::profile_path(config_root, &final_name);
    std::fs::create_dir_all(profiles_dir(config_root))?;
    if dest.exists() {
        return Err(AppError::msg("配置已存在"));
    }
    std::fs::create_dir_all(&dest)?;

    let cache = config_root.join("cache");
    std::fs::create_dir_all(&cache)?;
    let zip_path = cache.join("bepinex_download.zip");
    download_to_file(download_url, &zip_path)?;
    extract_zip(&zip_path, &dest)?;
    ensure_profile_layout(&dest)?;
    write_version_marker(&dest, version_label)?;
    let _ = std::fs::remove_file(&zip_path);
    Ok(final_name)
}

fn profile_exists(config_root: &Path, name: &str) -> bool {
    crate::paths::profile_path(config_root, name).exists()
}

fn write_version_marker(profile: &Path, version: &str) -> Result<()> {
    std::fs::write(profile.join("bepinex_version.txt"), version)?;
    Ok(())
}

/// Update framework files only; keep plugins and config.
pub fn update_profile_framework(
    profile: &Path,
    download_url: &str,
    version_label: &str,
) -> Result<()> {
    if !profile.is_dir() {
        return Err(AppError::msg("配置目录不存在"));
    }
    let cache = profile
        .parent()
        .and_then(|p| p.parent())
        .map(|p| p.join("cache"))
        .unwrap_or_else(|| profile.join("_cache"));
    std::fs::create_dir_all(&cache)?;
    let zip_path = cache.join("bepinex_update.zip");
    download_to_file(download_url, &zip_path)?;

    let staging = cache.join("bepinex_staging");
    if staging.exists() {
        std::fs::remove_dir_all(&staging)?;
    }
    std::fs::create_dir_all(&staging)?;
    extract_zip(&zip_path, &staging)?;

    // Replace framework pieces
    replace_dir(&staging.join("BepInEx").join("core"), &profile.join("BepInEx").join("core"))?;
    if staging.join("dotnet").is_dir() {
        replace_dir(&staging.join("dotnet"), &profile.join("dotnet"))?;
    }
    // patchers that ship with BepInEx (keep user patchers by merging? plan says replace framework)
    if staging.join("BepInEx").join("patchers").is_dir() {
        // Only replace known framework patchers; for simplicity replace entire patchers if empty of user mods
        // Safer: copy framework DLLs into patchers without wiping custom ones
        copy_dir_merge(
            &staging.join("BepInEx").join("patchers"),
            &profile.join("BepInEx").join("patchers"),
        )?;
    }

    for name in ["winhttp.dll", "doorstop_config.ini", ".doorstop_version", "changelog.txt"] {
        let src = staging.join(name);
        if src.is_file() {
            std::fs::copy(&src, profile.join(name))?;
        }
    }

    ensure_profile_layout(profile)?;
    write_version_marker(profile, version_label)?;
    let _ = std::fs::remove_file(&zip_path);
    let _ = std::fs::remove_dir_all(&staging);
    Ok(())
}

fn replace_dir(src: &Path, dest: &Path) -> Result<()> {
    if !src.is_dir() {
        return Ok(());
    }
    if dest.exists() {
        std::fs::remove_dir_all(dest)?;
    }
    copy_dir_all(src, dest)?;
    Ok(())
}

fn copy_dir_all(src: &Path, dest: &Path) -> Result<()> {
    std::fs::create_dir_all(dest)?;
    for entry in std::fs::read_dir(src)? {
        let entry = entry?;
        let ty = entry.file_type()?;
        let to = dest.join(entry.file_name());
        if ty.is_dir() {
            copy_dir_all(&entry.path(), &to)?;
        } else {
            std::fs::copy(entry.path(), to)?;
        }
    }
    Ok(())
}

fn copy_dir_merge(src: &Path, dest: &Path) -> Result<()> {
    std::fs::create_dir_all(dest)?;
    for entry in std::fs::read_dir(src)? {
        let entry = entry?;
        let ty = entry.file_type()?;
        let to = dest.join(entry.file_name());
        if ty.is_dir() {
            copy_dir_merge(&entry.path(), &to)?;
        } else {
            std::fs::copy(entry.path(), to)?;
        }
    }
    Ok(())
}

pub fn cache_download_url(url: &str, dest: &Path) -> Result<()> {
    download_to_file(url, dest)
}
