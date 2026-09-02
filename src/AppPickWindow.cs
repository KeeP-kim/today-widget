// 설치된 앱 고르기.
//
// Claude·ChatGPT·Gemini·Microsoft 365 같은 것들은 Store 앱이거나 Edge 웹앱이라
// **바로가기(.lnk) 파일이 아예 없다.** 파일 선택창으로는 담을 수가 없다.
// 그래서 셸이 아는 '설치된 앱' 목록을 그대로 보여주고, 고른 것을 가리키는
// 바로가기를 우리가 보관소에 만들어 둔다 (Apps.ImportApp).
//
// ★ 손으로 친 문자열은 받지 않는다 ★
//   여기서 고를 수 있는 것은 셸이 알려준 목록뿐이고, 그 이름표(AppUserModelID)도
//   Apps.IsSafeAppId 로 글자를 제한한다. 명령줄은 여전히 우리가 만들지 않는다.
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace DeskWidget
{
    internal sealed class AppPickWindow : Window
    {
        public Apps.InstalledApp Chosen;

        private readonly List<Apps.InstalledApp> _all;
        private readonly ListBox _list;
        private readonly TextBox _filter;
        private readonly TextBlock _empty;

        public AppPickWindow(double opacity)
        {
            Title = "설치된 앱";
            Width = 340;
            Height = 460;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            FontFamily = new FontFamily("Segoe UI, Malgun Gothic");
            Opacity = opacity < 0.4 ? 0.4 : opacity;

            _all = Apps.InstalledApps();

            var head = new TextBlock
            {
                Text = "즐겨찾기에 담을 앱",
                FontSize = 13,
                Foreground = Palette.Text,
                Margin = new Thickness(2, 0, 0, 8),
            };

            _filter = new TextBox
            {
                FontSize = 12.5,
                Background = Palette.Tile,
                Foreground = Palette.Text,
                CaretBrush = Palette.Text,
                BorderBrush = Palette.TileEdge,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(7, 4, 7, 4),
                Margin = new Thickness(0, 0, 0, 8),
            };
            _filter.TextChanged += delegate { Refill(); };

            _list = new ListBox
            {
                Background = Palette.Clear,
                BorderThickness = new Thickness(0),
                Foreground = Palette.Text,
                FontSize = 12.5,
            };
            _list.MouseDoubleClick += delegate { Take(); };
            _list.KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.Key == Key.Enter) { e.Handled = true; Take(); }
            };

            _empty = new TextBlock
            {
                Text = "설치된 앱을 읽지 못했습니다",
                FontSize = 12,
                Foreground = Palette.TextGhost,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 20, 0, 0),
                Visibility = Visibility.Collapsed,
            };

            var ok = MakeButton("담기", delegate { Take(); });
            var cancel = MakeButton("닫기", delegate { DialogResult = false; });
            cancel.Margin = new Thickness(7, 0, 0, 0);

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 9, 0, 0),
            };
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);

            var body = new Grid();
            body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            Grid.SetRow(head, 0); body.Children.Add(head);
            Grid.SetRow(_filter, 1); body.Children.Add(_filter);

            var host = new Grid();
            host.Children.Add(_list);
            host.Children.Add(_empty);
            Grid.SetRow(host, 2); body.Children.Add(host);

            Grid.SetRow(buttons, 3); body.Children.Add(buttons);

            Content = new Border
            {
                Child = body,
                Background = Palette.Card,
                BorderBrush = Palette.CardEdge,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(14),
                Padding = new Thickness(14, 12, 14, 12),
                Margin = new Thickness(4),
            };

            // 제목 표시줄이 없으므로 아무 데나 잡아 옮긴다
            MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e)
            {
                if (e.ClickCount != 1) return;
                try { DragMove(); }
                catch { }
            };
            KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.Key == Key.Escape) { e.Handled = true; DialogResult = false; }
            };

            Refill();
            Loaded += delegate { _filter.Focus(); };
        }

        private void Refill()
        {
            string q = (_filter.Text ?? "").Trim();
            _list.Items.Clear();

            foreach (var a in _all)
            {
                if (q.Length > 0 && a.Name.IndexOf(q, StringComparison.CurrentCultureIgnoreCase) < 0) continue;
                _list.Items.Add(new Item { App = a });
            }

            _empty.Visibility = (_list.Items.Count == 0) ? Visibility.Visible : Visibility.Collapsed;
            _empty.Text = (_all.Count == 0) ? "설치된 앱을 읽지 못했습니다" : "찾는 앱이 없습니다";
            if (_list.Items.Count > 0) _list.SelectedIndex = 0;
        }

        private void Take()
        {
            var it = _list.SelectedItem as Item;
            if (it == null) return;
            Chosen = it.App;
            DialogResult = true;
        }

        /// <summary>ListBox 에 이름만 보이게 담는 껍데기.</summary>
        private sealed class Item
        {
            public Apps.InstalledApp App;
            public override string ToString() { return App != null ? App.Name : ""; }
        }

        private static Border MakeButton(string text, Action onClick)
        {
            var t = new TextBlock
            {
                Text = text,
                FontSize = 12.5,
                Foreground = Palette.Text,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            var b = new Border
            {
                Child = t,
                Padding = new Thickness(14, 5, 14, 5),
                CornerRadius = new CornerRadius(7),
                Background = Palette.Tile,
                BorderBrush = Palette.TileEdge,
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand,
            };
            b.MouseEnter += delegate { b.Background = Palette.TileHover; };
            b.MouseLeave += delegate { b.Background = Palette.Tile; };
            b.MouseLeftButtonUp += delegate(object s, MouseButtonEventArgs e)
            {
                e.Handled = true;
                onClick();
            };
            b.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e) { e.Handled = true; };
            return b;
        }
    }
}
