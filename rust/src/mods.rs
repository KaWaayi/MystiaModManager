use crate::error::{AppError, Result};
use crate::profiles::profile_plugins;
use serde::Serialize;
use std::fs::File;
use std::io::copy;
use std::path::{Path, PathBuf};
use zip::ZipArchive;

#[derive(Serialize, Clone)]
pub struct ModInfo {
    pub name: String,
    pub path: String,
    pub enabled: bool,
    pub dll_count: usize,
}

pub fn list_mods(profile: &Path) -> Result<Vec<ModInfo>> {
    let plugins = profile_plugins(profile);
    if !plugins.is_dir() {
        return Ok(Vec::new());
    }
    let mut out = Vec::new();
    for entry in std::fs::read_dir(&plugins)? {
        let entry = entry?;
        if !entry.file_type()?.is_dir() {
            continue;
        }
        let name = entry.file_name().to_string_lossy().to_string();
        let path = entry.path();
        let (enabled, dll_count) = scan_mod_dir(&path);
        out.push(ModInfo {
            name,
            path: path.to_string_lossy().to_string(),
            enabled,
            dll_count,
        });
    }
    out.sort_by(|a, b| a.name.cmp(&b.name));
    Ok(out)
}

fn scan_mod_dir(dir: &Path) -> (bool, usize) {
    let mut has_on = false;
    let mut has_off = false;
    let mut count = 0;
    walk_dlls(dir, &mut |p| {
        count += 1;
        let s = p.to_string_lossy().to_lowercase();
        if s.ends_with(".dll.off") {
            has_off = true;
        } else if s.ends_with(".dll") {
            has_on = true;
        }
    });
    let enabled = has_on || !has_off;
    (enabled, count)
}

fn walk_dlls(dir: &Path, f: &mut dyn FnMut(PathBuf)) {
    let Ok(rd) = std::fs::read_dir(dir) else {
        return;
    };
    for entry in rd.flatten() {
        let path = entry.path();
        if path.is_dir() {
            walk_dlls(&path, f);
        } else {
            let name = path.file_name().and_then(|n| n.to_str()).unwrap_or("");
            let lower = name.to_lowercase();
            if lower.ends_with(".dll") || lower.ends_with(".dll.off") {
                f(path);
            }
        }
    }
}

pub fn set_mod_enabled(profile: &Path, mod_name: &str, enabled: bool) -> Result<()> {
    let dir = profile_plugins(profile).join(mod_name);
    if !dir.is_dir() {
        return Err(AppError::msg("模组目录不存在"));
    }
    let mut files = Vec::new();
    walk_dlls(&dir, &mut |p| files.push(p));
    for path in files {
        let name = path.file_name().and_then(|n| n.to_str()).unwrap_or("").to_string();
        let lower = name.to_lowercase();
        if enabled {
            if lower.ends_with(".dll.off") {
                let new_name = &name[..name.len() - 4]; // strip .off
                let dest = path.with_file_name(new_name);
                std::fs::rename(&path, dest)?;
            }
        } else if lower.ends_with(".dll") && !lower.ends_with(".dll.off") {
            let dest = path.with_extension("dll.off");
            std::fs::rename(&path, dest)?;
        }
    }
    Ok(())
}

pub fn uninstall_mod(profile: &Path, mod_name: &str) -> Result<()> {
    let dir = profile_plugins(profile).join(mod_name);
    if !dir.is_dir() {
        return Err(AppError::msg("模组目录不存在"));
    }
    std::fs::remove_dir_all(dir)?;
    Ok(())
}

pub fn install_mod_from_path(profile: &Path, source: &Path) -> Result<String> {
    if !source.exists() {
        return Err(AppError::msg("源文件不存在"));
    }
    let plugins = profile_plugins(profile);
    std::fs::create_dir_all(&plugins)?;

    if source.is_file() {
        let ext = source
            .extension()
            .and_then(|e| e.to_str())
            .unwrap_or("")
            .to_lowercase();
        if ext == "dll" {
            return install_loose_dll(profile, source);
        }
        if ext == "zip" {
            return install_zip(profile, source);
        }
        return Err(AppError::msg("仅支持 .dll 或 .zip"));
    }
    Err(AppError::msg("请选择文件"))
}

fn unique_mod_dir(plugins: &Path, base: &str) -> PathBuf {
    let mut candidate = plugins.join(base);
    let mut n = 2;
    while candidate.exists() {
        candidate = plugins.join(format!("{base} ({n})"));
        n += 1;
    }
    candidate
}

fn install_loose_dll(profile: &Path, dll: &Path) -> Result<String> {
    let stem = dll
        .file_stem()
        .and_then(|s| s.to_str())
        .unwrap_or("mod")
        .to_string();
    let plugins = profile_plugins(profile);
    let dest_dir = unique_mod_dir(&plugins, &stem);
    std::fs::create_dir_all(&dest_dir)?;
    let dest = dest_dir.join(dll.file_name().unwrap());
    std::fs::copy(dll, dest)?;
    Ok(dest_dir
        .file_name()
        .unwrap()
        .to_string_lossy()
        .to_string())
}

fn install_zip(profile: &Path, zip_path: &Path) -> Result<String> {
    let base_name = zip_path
        .file_stem()
        .and_then(|s| s.to_str())
        .unwrap_or("mod")
        .to_string();
    let plugins = profile_plugins(profile);

    let file = File::open(zip_path)?;
    let mut archive = ZipArchive::new(file)?;

    // Collect entries under any .../plugins/... path (case-insensitive)
    let mut plugin_entries: Vec<(String, usize)> = Vec::new();
    let mut loose_dlls: Vec<(String, usize)> = Vec::new();

    for i in 0..archive.len() {
        let file = archive.by_index(i)?;
        let name = file.name().replace('\\', "/");
        if name.ends_with('/') {
            continue;
        }
        let lower = name.to_lowercase();
        if let Some(idx) = find_plugins_segment(&lower) {
            // path after plugins/
            let after = &name[idx..];
            let after = after.trim_start_matches('/');
            if !after.is_empty() {
                plugin_entries.push((after.to_string(), i));
            }
        } else if lower.ends_with(".dll") && !name.contains('/') {
            loose_dlls.push((name, i));
        }
    }

    let dest_dir = unique_mod_dir(&plugins, &base_name);
    std::fs::create_dir_all(&dest_dir)?;

    if !plugin_entries.is_empty() {
        // Only take files from plugins folder, wrap in zip-named directory
        for (rel, idx) in plugin_entries {
            let mut file = archive.by_index(idx)?;
            // Strip any nested folder under plugins if it's a single package folder?
            // Plan: take files inside plugins, then wrap with zip name.
            // So plugins/Author-Mod/foo.dll -> dest_dir/Author-Mod/foo.dll
            // or plugins/foo.dll -> dest_dir/foo.dll
            let out = dest_dir.join(Path::new(&rel.replace('/', std::path::MAIN_SEPARATOR_STR)));
            if let Some(parent) = out.parent() {
                std::fs::create_dir_all(parent)?;
            }
            let mut outfile = File::create(&out)?;
            copy(&mut file, &mut outfile)?;
        }
    } else if !loose_dlls.is_empty() {
        for (name, idx) in loose_dlls {
            let mut file = archive.by_index(idx)?;
            let out = dest_dir.join(Path::new(&name).file_name().unwrap());
            let mut outfile = File::create(&out)?;
            copy(&mut file, &mut outfile)?;
        }
    } else {
        // Fallback: extract all dlls found anywhere into dest_dir (flat relative from last segment)
        let mut any = false;
        // Need to re-scan because we consumed? ZipArchive allows re-index.
        for i in 0..archive.len() {
            let mut file = archive.by_index(i)?;
            let name = file.name().replace('\\', "/");
            if name.ends_with('/') {
                continue;
            }
            if !name.to_lowercase().ends_with(".dll") {
                continue;
            }
            any = true;
            let fname = Path::new(&name)
                .file_name()
                .map(|f| f.to_owned())
                .unwrap_or_default();
            let out = dest_dir.join(fname);
            let mut outfile = File::create(&out)?;
            copy(&mut file, &mut outfile)?;
        }
        if !any {
            let _ = std::fs::remove_dir_all(&dest_dir);
            return Err(AppError::msg("压缩包中没有找到 plugins 内容或 dll"));
        }
    }

    Ok(dest_dir
        .file_name()
        .unwrap()
        .to_string_lossy()
        .to_string())
}

fn find_plugins_segment(lower_path: &str) -> Option<usize> {
    // return byte index of content after "plugins/"
    let markers = ["/plugins/", "plugins/"];
    for m in markers {
        if let Some(pos) = lower_path.find(m) {
            return Some(pos + m.len());
        }
    }
    None
}
