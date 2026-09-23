    private async Task OpenFileAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open Game / Package",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("PS4 / PS5 Applications")
                    { Patterns = new[] { "eboot.bin", "*.bin", "*.self", "*.elf" } },
                new FilePickerFileType("PlayStation Packages")
                    { Patterns = new[] { "*.pkg", "*.PKG" } },
                FilePickerFileTypes.All,
            },
        });

        var path = files.FirstOrDefault()?.TryGetLocalPath();
        if (!string.IsNullOrEmpty(path))
        {
            Launch(path, Path.GetFileName(path));
        }
    }
