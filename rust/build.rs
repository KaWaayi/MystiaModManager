fn main() {
    // Hand-written P/Invoke in ui/Native/NativeMethods.cs (net48 UTF-8 marshalling).
    println!("cargo:rerun-if-changed=src/ffi.rs");
}
