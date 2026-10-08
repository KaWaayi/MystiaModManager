use crate::error::{AppError, Result};
use crate::paths::profiles_dir;
use crate::profiles::{ensure_profile_layout, unique_profile_name, validate_profile_name};
use regex::Regex;
use serde::Serialize;
use std::fs::File;
use std::io::{Read, Write, copy};
use std::path::Path;
use std::time::{Duration, Instant};
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
    match stream_download(url, dest, true) {
        Ok(()) => Ok(()),
        Err(AppError::Msg(msg)) if msg == "slow" => {
            let Some(fallback) = server_framework_url(url) else {
                return Err(AppError::msg("官网下载过慢"));
            };
            let _ = std::fs::remove_file(dest);
            stream_download(&fallback, dest, false)
                .map_err(|e| AppError::msg(format!("已改从服务器下载，但仍失败: {e}")))
        }
        Err(e) => Err(e),
    }
}

fn server_framework_url(url: &str) -> Option<String> {
    if !url.contains("builds.bepinex.dev/") {
        return None;
    }
    let raw = url.rsplit('/').next()?.split('?').next()?;
    let name = raw.replace("%2B", "+").replace("%2b", "+");
    if !name.starts_with("BepInEx-") || !name.to_ascii_lowercase().ends_with(".zip") {
        return None;
    }
    Some(format!(
        "http://47.116.214.184/framework/file?name={}",
        query_escape(&name)
    ))
}

fn query_escape(value: &str) -> String {
    let mut out = String::new();
    for b in value.bytes() {
        match b {
            b'A'..=b'Z' | b'a'..=b'z' | b'0'..=b'9' | b'-' | b'_' | b'.' => out.push(b as char),
            _ => out.push_str(&format!("%{b:02X}")),
        }
    }
    out
}

fn stream_download(url: &str, dest: &Path, watch_speed: bool) -> Result<()> {
    let agent = ureq::AgentBuilder::new()
        .timeout_connect(Duration::from_secs(15))
        .timeout_read(Duration::from_secs(5))
        .build();
    let resp = agent
        .get(url)
        .set("User-Agent", "MystiaModManager/0.1")
        .call()
        .map_err(|e| AppError::Http(e.to_string()))?;
    if resp.status() != 200 {
        return Err(AppError::msg(format!("下载失败 {}", resp.status())));
    }
    let mut reader = resp.into_reader();
    if let Some(parent) = dest.parent() {
        std::fs::create_dir_all(parent)?;
    }
    let mut file = File::create(dest)?;
    let mut buf = [0u8; 64 * 1024];
    let mut window_start = Instant::now();
    let mut window_bytes = 0u64;
    let mut slow_for = Duration::ZERO;
    loop {
        match reader.read(&mut buf) {
            Ok(0) => break,
            Ok(n) => {
                file.write_all(&buf[..n])?;
                if watch_speed
                    && note_slow(
                        &mut window_start,
                        &mut window_bytes,
                        &mut slow_for,
                        n as u64,
                    )
                {
                    return Err(AppError::msg("slow"));
                }
            }
            Err(err)
                if watch_speed
                    && (err.kind() == std::io::ErrorKind::TimedOut
                        || err.kind() == std::io::ErrorKind::WouldBlock) =>
            {
                slow_for += Duration::from_secs(5);
                window_start = Instant::now();
                window_bytes = 0;
                if slow_for >= Duration::from_secs(15) {
                    return Err(AppError::msg("slow"));
                }
            }
            Err(err) => return Err(err.into()),
        }
    }
    Ok(())
}

    fn note_slow(
    window_start: &mut Instant,
    window_bytes: &mut u64,
    slow_for: &mut Duration,
    just_read: u64,
) -> bool {
    *window_bytes += just_read;
    let elapsed = window_start.elapsed();
    if elapsed < Duration::from_secs(1) {
        return false;
    }
    let rate = *window_bytes as f64 / elapsed.as_secs_f64();
    if rate < 150.0 * 1024.0 {
        *slow_for += elapsed;
    } else {
        *slow_for = Duration::ZERO;
    }
    *window_start = Instant::now();
    *window_bytes = 0;
    *slow_for >= Duration::from_secs(15)
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

#[cfg(test)]
mod tests {
    use super::server_framework_url;

    #[test]
    fn official_build_falls_back_to_server_file() {
        let url = server_framework_url(
            "https://builds.bepinex.dev/projects/bepinex_be/788/BepInEx-Unity.IL2CPP-win-x64-6.0.0-be.788%2B5b766a3.zip",
        )
        .unwrap();
        assert_eq!(
            url,
            "http://47.116.214.184/framework/file?name=BepInEx-Unity.IL2CPP-win-x64-6.0.0-be.788%2B5b766a3.zip"
        );
    }
}
