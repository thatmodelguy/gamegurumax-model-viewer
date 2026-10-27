# GameGuru MAX Model Viewer

A Windows desktop tool for browsing, organizing, and cleaning up your **GameGuru MAX** entity bank. View model previews, edit `.fpe` files, move or delete entities safely, and convert textures to DDS.

Designed for creators who keep a large `entitybank` and want a faster way to manage models without launching GameGuru MAX for every change.

## Features

- **Browse the entity bank** — folder tree + searchable model list (reads `.fpe` files)
- **Preview thumbnails** — uses GameGuru MAX `thumbbank` caches when available
- **Edit FPE fields** — description, model, scale, collision mode, static/dynamic, and all texture / material map slots (including multi-material `baseColorMap0`, `normalMap1`, etc.)
- **Move models** — relocate an entity and its associated files into another folder (or create a new one)
- **Delete safely** — sends files to the Recycle Bin and skips textures still used by other entities
- **Optimize textures** — convert PNG/JPEG to DDS (BC1 opaque / BC7 with transparency), optional max resolution, optional whole-folder pass
- **Unconventional size warning** — non-standard sizes (e.g. 512×1012) convert at their real dimensions so UVs still fit

## Requirements

### Standalone release (recommended)

Download the latest **Windows x64** zip from [Releases](https://github.com/thatmodelguy/gamegurumax-model-viewer/releases).  
Unzip and run `GameGuruMax.ModelViewer.exe` — no .NET install needed.

### Build from source

- Windows 10/11
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (Desktop / WPF workload)

```powershell
dotnet run --project src/GameGuruMax.ModelViewer/GameGuruMax.ModelViewer.csproj
```

Or open `GameGuruMax.ModelViewer.sln` in Visual Studio and press F5.

## Default paths

On first launch the app looks for:

`Documents\GameGuruApps\GameGuruMAX\Files`

That folder should contain `entitybank` and `thumbbank`.  
Use **Open Folder** if your GameGuru MAX Files location is different.

## Quick usage

1. Select a folder in the left tree
2. Click a model to preview it and inspect associated files / FPE fields
3. **Save FPE** after edits
4. **Move** / **Delete** to organize or remove models
5. **Optimize** to convert textures to DDS (leave Max resolution on *Original* to keep exact pixel sizes)

## Notes

- Deletes go to the Recycle Bin when possible
- Shared textures are not deleted if another FPE still references them
- Surface maps that are already DDS are skipped for conversion, but can still be resized if you set a max resolution
- This tool does **not** include a rotatable 3D DBO viewport yet (preview uses cached images)

## License

Private project for now. Contact the author before redistributing.
