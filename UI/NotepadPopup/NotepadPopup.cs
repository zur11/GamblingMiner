using Godot;
using System.Collections.Generic;
#nullable enable

namespace UI.NotepadPopup
{
	public partial class NotepadPopup : Panel
	{
		private NotepadService? _notepadService;
		private OptionButton _loadDropdown = null!;
		private Button _deleteBtn = null!;
		private TextEdit _contentInput = null!;
		private LineEdit _nameInput = null!;
		private Button _saveBtn = null!;

		private const string DropdownPlaceholder = "— select a note to load —";

		public override void _Ready()
		{
			_notepadService = GetNodeOrNull<NotepadService>("/root/NotepadService");

			SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
			GrowHorizontal = GrowDirection.Both;
			GrowVertical = GrowDirection.Both;
			Visible = false;

			var margin = new MarginContainer();
			margin.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
			margin.GrowHorizontal = GrowDirection.Both;
			margin.GrowVertical = GrowDirection.Both;
			margin.AddThemeConstantOverride("margin_left", 80);
			margin.AddThemeConstantOverride("margin_top", 60);
			margin.AddThemeConstantOverride("margin_right", 80);
			margin.AddThemeConstantOverride("margin_bottom", 60);
			AddChild(margin);

			var vbox = new VBoxContainer();
			vbox.SizeFlagsHorizontal = SizeFlags.ExpandFill;
			vbox.SizeFlagsVertical = SizeFlags.ExpandFill;
			vbox.AddThemeConstantOverride("separation", 14);
			margin.AddChild(vbox);

			// Title bar
			var topBar = new HBoxContainer();
			topBar.AddThemeConstantOverride("separation", 16);
			var title = new Label { Text = "Notepad" };
			title.AddThemeFontSizeOverride("font_size", 36);
			title.SizeFlagsHorizontal = SizeFlags.ExpandFill;
			var closeBtn = new Button { Text = "✕ Close" };
			closeBtn.AddThemeFontSizeOverride("font_size", 24);
			closeBtn.Pressed += Close;
			topBar.AddChild(title);
			topBar.AddChild(closeBtn);
			vbox.AddChild(topBar);

			// Seed words warning
			var warning = new Label
			{
				Text = "⚠ Never store your seed words in the In-Game Notepad or any digital document — not even this app. If your paper is lost, your BTC cannot be recovered.",
				AutowrapMode = TextServer.AutowrapMode.Word
			};
			warning.AddThemeFontSizeOverride("font_size", 18);
			vbox.AddChild(warning);

			// Load existing note
			var loadRow = new HBoxContainer();
			loadRow.AddThemeConstantOverride("separation", 12);
			var loadLabel = new Label { Text = "Saved notes:" };
			loadLabel.AddThemeFontSizeOverride("font_size", 20);
			_loadDropdown = new OptionButton();
			_loadDropdown.SizeFlagsHorizontal = SizeFlags.ExpandFill;
			_loadDropdown.AddThemeFontSizeOverride("font_size", 20);
			_deleteBtn = new Button { Text = "Delete", Disabled = true };
			_deleteBtn.AddThemeFontSizeOverride("font_size", 20);
			loadRow.AddChild(loadLabel);
			loadRow.AddChild(_loadDropdown);
			loadRow.AddChild(_deleteBtn);
			vbox.AddChild(loadRow);

			_loadDropdown.ItemSelected += OnNoteSelected;
			_deleteBtn.Pressed += OnDeletePressed;

			// Content area
			var contentLabel = new Label { Text = "Note content:" };
			contentLabel.AddThemeFontSizeOverride("font_size", 20);
			vbox.AddChild(contentLabel);

			_contentInput = new TextEdit();
			_contentInput.CustomMinimumSize = new Vector2(0, 260);
			_contentInput.SizeFlagsVertical = SizeFlags.ExpandFill;
			_contentInput.AddThemeFontSizeOverride("font_size", 20);
			_contentInput.TextChanged += UpdateSaveButton;
			vbox.AddChild(_contentInput);

			// Save row
			var saveRow = new HBoxContainer();
			saveRow.AddThemeConstantOverride("separation", 12);
			var nameLabel = new Label { Text = "Save as:" };
			nameLabel.AddThemeFontSizeOverride("font_size", 20);
			_nameInput = new LineEdit();
			_nameInput.PlaceholderText = "note name...";
			_nameInput.SizeFlagsHorizontal = SizeFlags.ExpandFill;
			_nameInput.AddThemeFontSizeOverride("font_size", 20);
			_nameInput.TextChanged += _ => UpdateSaveButton();
			_saveBtn = new Button { Text = "Save", Disabled = true };
			_saveBtn.AddThemeFontSizeOverride("font_size", 22);
			_saveBtn.Pressed += OnSavePressed;
			saveRow.AddChild(nameLabel);
			saveRow.AddChild(_nameInput);
			saveRow.AddChild(_saveBtn);
			vbox.AddChild(saveRow);

			RefreshDropdown();
		}

		public void Open()
		{
			RefreshDropdown();
			Visible = true;
		}

		private void Close() => Visible = false;

		private void RefreshDropdown(string? selectName = null)
		{
			_loadDropdown.Clear();
			_loadDropdown.AddItem(DropdownPlaceholder);

			IReadOnlyList<string> names = _notepadService?.GetAllNames() ?? new List<string>();
			foreach (string name in names)
				_loadDropdown.AddItem(name);

			if (selectName != null)
			{
				for (int i = 1; i < _loadDropdown.ItemCount; i++)
				{
					if (_loadDropdown.GetItemText(i) == selectName)
					{
						_loadDropdown.Select(i);
						_deleteBtn.Disabled = false;
						return;
					}
				}
			}

			_loadDropdown.Select(0);
			_deleteBtn.Disabled = true;
		}

		private void OnNoteSelected(long idx)
		{
			if (idx <= 0)
			{
				_deleteBtn.Disabled = true;
				return;
			}

			string name = _loadDropdown.GetItemText((int)idx);
			string content = _notepadService?.LoadNote(name) ?? string.Empty;
			_nameInput.Text = name;
			_contentInput.Text = content;
			_deleteBtn.Disabled = false;
			UpdateSaveButton();
		}

		private void OnSavePressed()
		{
			string name = _nameInput.Text.Trim();
			if (string.IsNullOrEmpty(name)) return;
			_notepadService?.SaveNote(name, _contentInput.Text);
			RefreshDropdown(name);
		}

		private void OnDeletePressed()
		{
			int idx = _loadDropdown.Selected;
			if (idx <= 0) return;
			string name = _loadDropdown.GetItemText(idx);
			_notepadService?.DeleteNote(name);
			_contentInput.Text = string.Empty;
			_nameInput.Text = string.Empty;
			RefreshDropdown();
			UpdateSaveButton();
		}

		private void UpdateSaveButton()
		{
			_saveBtn.Disabled = string.IsNullOrEmpty(_nameInput.Text.Trim())
			                 || _contentInput.Text.Length == 0;
		}
	}
}
