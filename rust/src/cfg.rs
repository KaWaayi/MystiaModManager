use crate::error::{AppError, Result};
use serde::{Deserialize, Serialize};
use std::path::{Path, PathBuf};

#[derive(Serialize, Clone)]
pub struct CfgSetting {
    pub section: String,
    pub key: String,
    pub value: String,
    pub original: String,
    pub description: String,
    pub type_name: String,
    pub default_value: String,
    pub hint: String,
    pub options: Vec<String>,
    pub line_index: usize,
}

#[derive(Deserialize)]
pub struct CfgUpdate {
    pub line_index: usize,
    pub value: String,
}

struct CfgText {
    bom: bool,
    lines: Vec<String>,
    endings: Vec<String>,
    settings: Vec<CfgSetting>,
}

pub fn list_cfg_files(profile: &Path) -> Result<Vec<String>> {
    let dir = config_dir(profile);
    if !dir.is_dir() {
        return Ok(Vec::new());
    }
    let mut names = Vec::new();
    for entry in std::fs::read_dir(dir)? {
        let entry = entry?;
        if !entry.file_type()?.is_file() {
            continue;
        }
        let name = entry.file_name().to_string_lossy().to_string();
        if name.to_lowercase().ends_with(".cfg") {
            names.push(name);
        }
    }
    names.sort();
    Ok(names)
}

pub fn load_cfg(profile: &Path, file_name: &str) -> Result<Vec<CfgSetting>> {
    Ok(read_cfg(&cfg_path(profile, file_name)?)?.settings)
}

pub fn save_cfg(profile: &Path, file_name: &str, updates: &[CfgUpdate]) -> Result<()> {
    let path = cfg_path(profile, file_name)?;
    let mut doc = read_cfg(&path)?;
    let mut changed = false;
    for update in updates {
        let Some(line) = doc.lines.get_mut(update.line_index) else {
            continue;
        };
        let Some(eq) = line.find('=') else { continue };
        if eq == 0 {
            continue;
        }
        let current = line[eq + 1..].trim();
        if current == update.value {
            continue;
        }
        let left = line[..eq].trim_end();
        *line = format!("{left} = {}", update.value);
        changed = true;
    }
    if !changed {
        return Ok(());
    }
    let mut out = Vec::new();
    if doc.bom {
        out.extend_from_slice(&[0xEF, 0xBB, 0xBF]);
    }
    for (i, line) in doc.lines.iter().enumerate() {
        out.extend(line.as_bytes());
        if let Some(ending) = doc.endings.get(i) {
            out.extend(ending.as_bytes());
        }
    }
    std::fs::write(path, out)?;
    Ok(())
}

fn config_dir(profile: &Path) -> PathBuf {
    profile.join("BepInEx").join("config")
}

fn cfg_path(profile: &Path, file_name: &str) -> Result<PathBuf> {
    if file_name.is_empty()
        || file_name.contains(['/', '\\', ':', '*', '?', '"', '<', '>', '|'])
        || !file_name.to_lowercase().ends_with(".cfg")
    {
        return Err(AppError::msg("配置文件名无效"));
    }
    let path = config_dir(profile).join(file_name);
    if !path.is_file() {
        return Err(AppError::msg("配置文件不存在"));
    }
    Ok(path)
}

fn read_cfg(path: &Path) -> Result<CfgText> {
    let bytes = std::fs::read(path)?;
    let bom = bytes.len() >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
    let payload = if bom { &bytes[3..] } else { &bytes[..] };
    let text = String::from_utf8_lossy(payload);
    let (lines, endings) = split_keep_endings(&text);

    let mut section = String::new();
    let mut description = String::new();
    let mut type_name = String::new();
    let mut default_value = String::new();
    let mut hint = String::new();
    let mut options = Vec::new();
    let mut settings = Vec::new();

    for (i, raw) in lines.iter().enumerate() {
        let trimmed = raw.trim();
        if trimmed.starts_with('[') && trimmed.ends_with(']') && trimmed.len() > 2 {
            section = trimmed[1..trimmed.len() - 1].to_string();
            clear_pending(&mut description, &mut type_name, &mut default_value, &mut hint, &mut options);
            continue;
        }
        if let Some(note) = trimmed.strip_prefix("##") {
            let note = note.trim();
            if !note.is_empty() {
                if !description.is_empty() {
                    description.push(' ');
                }
                description.push_str(note);
            }
            continue;
        }
        if let Some(note) = trimmed.strip_prefix('#') {
            let note = note.trim();
            if let Some(rest) = strip_prefix_ignore_ascii_case(note, "Setting type:") {
                type_name = rest.trim().to_string();
            } else if let Some(rest) = strip_prefix_ignore_ascii_case(note, "Default value:") {
                default_value = rest.trim().to_string();
            } else if let Some(rest) = strip_prefix_ignore_ascii_case(note, "Acceptable value range:") {
                hint = rest.trim().to_string();
            } else if let Some(rest) = strip_prefix_ignore_ascii_case(note, "Acceptable values:") {
                let raw_options = rest.trim();
                if !raw_options.is_empty() && raw_options.len() <= 180 {
                    options = raw_options
                        .split(',')
                        .map(str::trim)
                        .filter(|s| !s.is_empty())
                        .map(str::to_string)
                        .collect();
                } else if raw_options.len() > 180 {
                    hint = "可选值很多，直接填写".into();
                }
            }
            continue;
        }
        let Some(eq) = raw.find('=') else { continue };
        if eq == 0 {
            continue;
        }
        let key = raw[..eq].trim();
        if key.is_empty() || key.starts_with('#') {
            continue;
        }
        let value = raw[eq + 1..].trim().to_string();
        settings.push(CfgSetting {
            section: section.clone(),
            key: key.to_string(),
            value: value.clone(),
            original: value,
            description: description.clone(),
            type_name: type_name.clone(),
            default_value: default_value.clone(),
            hint: hint.clone(),
            options: options.clone(),
            line_index: i,
        });
        clear_pending(&mut description, &mut type_name, &mut default_value, &mut hint, &mut options);
    }

    Ok(CfgText {
        bom,
        lines,
        endings,
        settings,
    })
}

fn split_keep_endings(text: &str) -> (Vec<String>, Vec<String>) {
    let mut lines = Vec::new();
    let mut endings = Vec::new();
    let mut cursor = 0;
    let bytes_len = text.len();
    while cursor <= bytes_len {
        let rest = &text[cursor..];
        let Some(rel) = rest.find('\n') else {
            lines.push(rest.trim_end_matches('\r').to_string());
            endings.push(String::new());
            break;
        };
        let mut content = &rest[..rel];
        let mut ending = "\n";
        if content.ends_with('\r') {
            content = &content[..content.len() - 1];
            ending = "\r\n";
        }
        lines.push(content.to_string());
        endings.push(ending.to_string());
        cursor += rel + 1;
        if cursor == bytes_len {
            lines.push(String::new());
            endings.push(String::new());
            break;
        }
    }
    (lines, endings)
}

fn clear_pending(
    description: &mut String,
    type_name: &mut String,
    default_value: &mut String,
    hint: &mut String,
    options: &mut Vec<String>,
) {
    description.clear();
    type_name.clear();
    default_value.clear();
    hint.clear();
    options.clear();
}

fn strip_prefix_ignore_ascii_case<'a>(text: &'a str, prefix: &str) -> Option<&'a str> {
    if text.len() < prefix.len() {
        return None;
    }
    if text[..prefix.len()].eq_ignore_ascii_case(prefix) {
        Some(&text[prefix.len()..])
    } else {
        None
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn unchanged_save_keeps_bytes() {
        let dir = std::env::temp_dir().join("mystia-cfg-unit");
        let _ = std::fs::remove_dir_all(&dir);
        let cfg_dir = dir.join("BepInEx").join("config");
        std::fs::create_dir_all(&cfg_dir).unwrap();
        let raw = "\u{feff}[Config]\r\n\r\n## 说明\r\n# Setting type: Boolean\r\n# Default value: true\r\nFlag = true\r\n";
        std::fs::write(cfg_dir.join("Sample.cfg"), raw.as_bytes()).unwrap();
        let loaded = load_cfg(&dir, "Sample.cfg").unwrap();
        let updates: Vec<CfgUpdate> = loaded
            .iter()
            .map(|s| CfgUpdate {
                line_index: s.line_index,
                value: s.value.clone(),
            })
            .collect();
        save_cfg(&dir, "Sample.cfg", &updates).unwrap();
        let after = std::fs::read(cfg_dir.join("Sample.cfg")).unwrap();
        assert_eq!(after, raw.as_bytes());
        assert_eq!(loaded.len(), 1);
        assert_eq!(loaded[0].key, "Flag");
        assert_eq!(loaded[0].type_name, "Boolean");
    }
}
