using System.Collections.ObjectModel;
using System.Windows;
using Microsoft.Win32;

namespace LangBoost;

/// <summary>
/// Focusable editor for the curated English phrases (separate from the overlay because the overlay
/// uses WS_EX_NOACTIVATE and cannot receive keyboard focus). Edits/removals are written back to the
/// list passed in; "Save to file…" exports a .jsonl and clears the list (the session's phrases are
/// kept in memory only until saved).
/// </summary>
public partial class PhrasesWindow : Window
{
    private readonly List<string> _phrases;
    private readonly ObservableCollection<PhraseRow> _rows;

    public PhrasesWindow(List<string> phrases)
    {
        _phrases = phrases;
        InitializeComponent();

        _rows = new ObservableCollection<PhraseRow>(phrases.Select(p => new PhraseRow { Text = p }));
        Rows.ItemsSource = _rows;
        UpdateEmptyState();
    }

    private void OnDeleteRow(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: PhraseRow row })
        {
            _rows.Remove(row);
            UpdateEmptyState();
        }
    }

    private void UpdateEmptyState()
    {
        EmptyText.Visibility = _rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        SaveButton.IsEnabled = _rows.Count > 0;
    }

    /// <summary>Current non-empty, trimmed phrases in display order.</summary>
    private List<string> CurrentPhrases() =>
        _rows.Select(r => (r.Text ?? "").Trim())
             .Where(s => s.Length > 0)
             .ToList();

    private void OnSave(object sender, RoutedEventArgs e)
    {
        var phrases = CurrentPhrases();
        if (phrases.Count == 0) return;

        var dlg = new SaveFileDialog
        {
            Filter = "JSON Lines (*.jsonl)|*.jsonl",
            DefaultExt = "jsonl",
            FileName = "phrases.jsonl",
            AddExtension = true,
        };
        if (dlg.ShowDialog(this) != true) return;

        try
        {
            PhrasesExporter.Write(dlg.FileName, phrases);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Failed to save the file:\n" + ex.Message,
                "LangBoost", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        _phrases.Clear(); // saved → drop the phrases from memory
        Close();
    }

    private void OnClose(object sender, RoutedEventArgs e)
    {
        // Persist edits/removals so they survive reopening the editor within the session.
        _phrases.Clear();
        _phrases.AddRange(CurrentPhrases());
        Close();
    }
}

/// <summary>One editable row; <see cref="Text"/> is two-way bound to the row's TextBox.</summary>
internal sealed class PhraseRow
{
    public string Text { get; set; } = "";
}
