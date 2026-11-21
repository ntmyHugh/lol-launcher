using System;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Input;
using LolLauncher.ViewModels;

namespace LolLauncher
{
    public partial class MainWindow : Window
    {
        private readonly MainViewModel _viewModel;

        public MainWindow(MainViewModel viewModel)
        {
            InitializeComponent();

            _viewModel = viewModel;
            DataContext = viewModel;

            _viewModel.CloseRequested += OnCloseRequested;

            Loaded += OnLoaded;
            Closed += OnClosed;
        }

        private void OnCloseRequested(object sender, EventArgs e)
        {
            Close();
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            _viewModel.Logs.CollectionChanged += OnLogsChanged;

            if (_viewModel.Logs.Count > 0)
            {
                LogList.ScrollIntoView(_viewModel.Logs[_viewModel.Logs.Count - 1]);
            }

            Dispatcher.BeginInvoke(new Action(() => _viewModel.AutoDetectIfNeeded()),
                System.Windows.Threading.DispatcherPriority.Background);
        }

        private void OnClosed(object sender, EventArgs e)
        {
            _viewModel.Logs.CollectionChanged -= OnLogsChanged;
            _viewModel.CloseRequested -= OnCloseRequested;
            _viewModel.Dispose();
        }

        private void OnLogsChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action == NotifyCollectionChangedAction.Add && LogList.Items.Count > 0)
            {
                LogList.ScrollIntoView(LogList.Items[LogList.Items.Count - 1]);
            }
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed)
            {
                return;
            }

            try
            {
                DragMove();
            }
            catch (InvalidOperationException)
            {
                // 拖拽期间窗口状态变化时忽略。
            }
        }

        private void Minimize_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void Window_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop)
                ? DragDropEffects.Copy
                : DragDropEffects.None;
            e.Handled = true;
        }

        private void Window_Drop(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                return;
            }

            var paths = e.Data.GetData(DataFormats.FileDrop) as string[];
            _viewModel.HandleDroppedPaths(paths);
            e.Handled = true;
        }
    }
}
