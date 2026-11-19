using System.Collections;
using System.ComponentModel;
using System.Globalization;
using System.Windows.Data;
using Waypoint.Models;

namespace Waypoint.ViewModels;

/// <summary>Groups files into Today, Yesterday, Last week and so on, by the date the tab is sorted on.</summary>
public sealed class DateGroupDescription : GroupDescription
{
    static readonly string[] Labels =
    [
        "Today",
        "Yesterday",
        "Last week",
        "Less than a month ago",
        "Less than a year ago",
        "Over a year ago",
        "Unknown date",
    ];

    readonly TabViewModel tab;

    public DateGroupDescription(TabViewModel tab)
    {
        this.tab = tab;
        CustomSort = new GroupOrder(tab);
    }

    public override object GroupNameFromItem(object item, int level, CultureInfo culture)
    {
        var file = (FileItem)item;
        return Labels[Bucket(tab.SortColumn == SortColumn.Created ? file.Created : file.Modified)];
    }

    static int Bucket(DateTime? date)
    {
        if (date is not { } value)
            return Labels.Length - 1;
        var today = DateTime.Today;
        if (value >= today)
            return 0;
        if (value >= today.AddDays(-1))
            return 1;
        if (value >= today.AddDays(-7))
            return 2;
        if (value >= today.AddMonths(-1))
            return 3;
        if (value >= today.AddYears(-1))
            return 4;
        return 5;
    }

    /// <summary>Newest group first, unless the list is sorted by date oldest first.</summary>
    sealed class GroupOrder(TabViewModel tab) : IComparer
    {
        public int Compare(object? x, object? y)
        {
            int result = Index(x).CompareTo(Index(y));
            bool oldestFirst = tab.SortColumn is SortColumn.Modified or SortColumn.Created && !tab.SortDescending;
            return oldestFirst ? -result : result;
        }

        static int Index(object? group) => Array.IndexOf(Labels, (group as CollectionViewGroup)?.Name as string);
    }
}
