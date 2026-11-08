using System.Globalization;
using System.Windows.Media;
using Waypoint.Services;
using Waypoint.ViewModels;

namespace Waypoint.Models;

public sealed class FileItem : ObservableObject
{
    ImageSource? icon;
    ImageSource? largeIcon;
    double usedPercent;
    long size;
    DateTime? modified;
    DateTime? created;
    bool isHidden;
    string sizeText = "";

    public required string Name { get; init; }
    public required string FullPath { get; init; }
    public bool IsFolder { get; init; }
    public bool IsDrive { get; init; }
    public string TypeName { get; init; } = "";

    /// <summary>True when the generic icon should later be replaced by the file's own icon.</summary>
    public bool NeedsOwnIcon { get; init; }

    public ImageSource? Icon { get => icon; set => SetProperty(ref icon, value); }

    public long Size { get => size; private set => SetProperty(ref size, value); }

    public bool IsHidden { get => isHidden; private set => SetProperty(ref isHidden, value); }

    public string SizeText { get => sizeText; private set => SetProperty(ref sizeText, value); }

    public DateTime? Modified
    {
        get => modified;
        private set
        {
            if (SetProperty(ref modified, value))
                OnPropertyChanged(nameof(ModifiedText));
        }
    }

    public string ModifiedText => Modified?.ToString("g", CultureInfo.CurrentCulture) ?? "";

    public DateTime? Created
    {
        get => created;
        private set
        {
            if (SetProperty(ref created, value))
                OnPropertyChanged(nameof(CreatedText));
        }
    }

    public string CreatedText => Created?.ToString("g", CultureInfo.CurrentCulture) ?? "";

    /// <summary>Drive tiles only; loaded on first use.</summary>
    public ImageSource? LargeIcon => IsDrive ? largeIcon ??= ShellIcons.GetLarge(FullPath) ?? Icon : null;

    public bool HasCapacity => IsDrive && Size > 0;

    public bool IsNearlyFull => HasCapacity && UsedPercent >= 90;

    public double UsedPercent
    {
        get => usedPercent;
        private set
        {
            if (!SetProperty(ref usedPercent, value))
                return;
            OnPropertyChanged(nameof(HasCapacity));
            OnPropertyChanged(nameof(IsNearlyFull));
        }
    }

    public static FileItem Create(FileEntry entry)
    {
        var item = new FileItem
        {
            Name = entry.Name,
            FullPath = entry.Path,
            IsFolder = entry.IsFolder,
            IsDrive = entry.IsDrive,
            TypeName = entry.TypeName ?? ShellIcons.GetTypeName(entry.Path, entry.IsFolder),
            NeedsOwnIcon = !entry.IsFolder && ShellIcons.HasOwnIcon(entry.Path),
            Icon = entry.IsDrive
                ? ShellIcons.GetForPath(entry.Path) ?? ShellIcons.GetGeneric(entry.Path, true)
                : ShellIcons.GetGeneric(entry.Path, entry.IsFolder),
        };
        item.Update(entry);
        return item;
    }

    public void Update(FileEntry entry)
    {
        Size = entry.Size;
        Modified = entry.Modified;
        Created = entry.Created;
        IsHidden = entry.Hidden;
        UsedPercent = entry.UsedPercent;
        SizeText = entry.SizeText ?? (entry.IsFolder ? "" : Format.Size(entry.Size));
    }
}
