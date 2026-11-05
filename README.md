# DelFast

DelFast is a lightweight Windows utility for instantly deleting files and folders without the delays caused by Windows Explorer calculations and animations.

> **This fork** replaces the single-threaded `Directory.Delete` with a parallel native engine (`Core/FastDeleter.cs`) and adds a command line tool: `dfast <path> [path...] [-y] [-t threads]`.
> Measured about 3x to 5x faster than the original (`Bench/`, run `dfast-bench selftest` and `dfast-bench bench`).
> Junctions and symlinks are removed as links and never followed. Drive roots, Windows and profile folders are refused.
> Install with `.\install.ps1` (no admin; remove with `-Uninstall`): `dfast` on PATH plus an Explorer right-click entry with one confirmation per selection. Building needs the .NET SDK and Visual Studio C++ tools. `dfast` and the right-click tool are native and need no runtime; the window app needs the .NET 10 Desktop Runtime.

It is designed to be fast, simple, and portable.

---

## Features

* Instant deletion of files and folders
* Multi-select support (delete multiple items at once)
* No installer, single EXE
* Extremely small size (about 188 KB)
* Minimal CPU usage
* Clean and simple UI
* Permanent deletion (no Recycle Bin)

---

## Why DelFast

Windows Explorer wastes time calculating file sizes and updating the UI before deleting items.
DelFast skips these steps and removes files immediately.

Perfect for large folders like:

* node_modules
* build or dist folders
* temp directories
* logs and cache files

---

## Usage

1. Launch DelFast.exe
2. Select files or folders
3. Click Delete
4. Confirm deletion

That’s it.

---

## System Requirements

* Windows 10 or newer
* .NET Framework (installed by default on most systems)

---

## Download

Official website:
https://delfastsite.web.app/
