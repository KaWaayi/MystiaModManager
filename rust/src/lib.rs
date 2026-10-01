pub mod bepinex;
pub mod doorstop;
pub mod error;
pub mod ffi;
pub mod manager;
pub mod mods;
pub mod paths;
pub mod process;
pub mod profiles;

#[cfg(test)]
mod tests {
    use crate::paths;

    #[test]
    fn detect_game_on_this_machine() {
        let game = paths::detect_game_path();
        println!("game={game:?}");
        if let Some(p) = game {
            assert!(p.join(paths::GAME_EXE).is_file());
            let defaults = paths::default_paths_for_game(&p).unwrap();
            assert!(!defaults.manager_path.is_empty());
            assert_ne!(defaults.manager_path, defaults.config_root);
        }
    }

    #[test]
    fn list_bepinex_builds() {
        let builds = crate::bepinex::list_il2cpp_win_x64_builds().expect("fetch builds");
        assert!(!builds.is_empty());
        assert!(builds[0].url.contains("IL2CPP-win-x64-6"));
        println!("latest=#{} {}", builds[0].build_id, builds[0].version);
    }
}
