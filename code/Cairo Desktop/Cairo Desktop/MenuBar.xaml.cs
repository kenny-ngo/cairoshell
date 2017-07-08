using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;
using CairoDesktop.Interop;
using System.Windows.Input;
using CairoDesktop.SupportingClasses;
using System.Windows.Interop;
using CairoDesktop.Configuration;
using CairoDesktop.Common;

namespace CairoDesktop
{
    public partial class MenuBar
    {
        // AppBar properties
        private WindowInteropHelper helper;
        private IntPtr handle;
        private int appbarMessageId = -1;

        // AppGrabber instance
        public AppGrabber.AppGrabber appGrabber = AppGrabber.AppGrabber.Instance;

        // True if system tray failed to load
        public bool SystemTrayFailure = false;

        private String fileManger = Environment.ExpandEnvironmentVariables(Settings.FileManager);

        public MenuBar()
        {
            InitializeComponent();

            Width = SystemParameters.WorkArea.Width;

            setupPlaces();

            setupSearch();

            initializeClock();

            setupPrograms();
        }

        private void setupPrograms()
        {
            // Set Quick Launch and Uncategorized categories to not show in menu
            AppGrabber.Category ql = appGrabber.CategoryList.GetCategory("Quick Launch");
            if (ql != null)
            {
                ql.ShowInMenu = false;
            }
            AppGrabber.Category uncat = appGrabber.CategoryList.GetCategory("Uncategorized");
            if (uncat != null)
            {
                uncat.ShowInMenu = false;
            }

            // Set Programs Menu to use appGrabber's ProgramList as its source
            categorizedProgramsList.ItemsSource = appGrabber.CategoryList;

            // set tab based on user preference
            int i = categorizedProgramsList.Items.IndexOf(appGrabber.CategoryList.GetCategory(Settings.DefaultProgramsCategory));
            categorizedProgramsList.SelectedIndex = i;
        }

        private void setupPlaces()
        {
            // Set username
            string username = Environment.UserName.Replace("_", "__");
            miUserName.Header = username;

            // Only show Downloads folder on Vista or greater
            if (Environment.OSVersion.Version.Major < 6)
            {
                PlacesDownloadsItem.Visibility = Visibility.Collapsed;
            }
        }

        private void setupSearch()
        {
            this.CommandBindings.Add(new CommandBinding(CustomCommands.OpenSearchResult, ExecuteOpenSearchResult));

            // Show the search button only if the service is running
            if (WindowsServices.QueryStatus("WSearch") == ServiceStatus.Running)
            {
                setSearchProvider();
            }
            else
            {
                CairoSearchMenu.Visibility = Visibility.Collapsed;
                DispatcherTimer searchcheck = new DispatcherTimer(DispatcherPriority.Background, this.Dispatcher);
                searchcheck.Interval = new TimeSpan(0, 0, 5);
                searchcheck.Tick += searchcheck_Tick;
                searchcheck.Start();
            }
        }

        private void searchcheck_Tick(object sender, EventArgs e)
        {
            if (WindowsServices.QueryStatus("WSearch") == ServiceStatus.Running)
            {
                setSearchProvider();
                CairoSearchMenu.Visibility = Visibility.Visible;
                (sender as DispatcherTimer).Stop();
            }
            else
            {
                CairoSearchMenu.Visibility = Visibility.Collapsed;
            }
        }

        private void setSearchProvider()
        {
            ObjectDataProvider vistaSearchProvider = new ObjectDataProvider();
            vistaSearchProvider.ObjectType = typeof(VistaSearchProvider.VistaSearchProviderHelper);
            CairoSearchMenu.DataContext = vistaSearchProvider;

            Binding bSearchText = new Binding("SearchText");
            bSearchText.Mode = BindingMode.Default;
            bSearchText.UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged;

            Binding bSearchResults = new Binding("Results");
            bSearchResults.Mode = BindingMode.Default;
            bSearchResults.IsAsync = true;

            searchStr.SetBinding(TextBox.TextProperty, bSearchText);
            lstSearchResults.SetBinding(ListView.ItemsSourceProperty, bSearchResults);
        }

        private void shutdown()
        {
            Application.Current.Shutdown();
        }

        private void LaunchProgram(object sender, RoutedEventArgs e)
        {
            MenuItem item = (MenuItem)sender;
            if (!Shell.StartProcess(item.CommandParameter.ToString()))
            {
                CairoMessage.Show("The file could not be found.  If you just removed this program, try removing it from the App Grabber to make the icon go away.", "Oops!", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        #region Date/time
        /// <summary>
        /// Initializes the dispatcher timers to updates the time and date bindings
        /// </summary>
        private void initializeClock()
        {
            // initial display
            clock_Tick();

            // Create our timer for clock
            DispatcherTimer clock = new DispatcherTimer(new TimeSpan(0, 0, 0, 0, 500), DispatcherPriority.Background, delegate
            {
                clock_Tick();
            }, this.Dispatcher);
        }

        private void clock_Tick()
        {
            string timeFormat = Settings.TimeFormat;
            if (string.IsNullOrEmpty(timeFormat))
            {
                timeFormat = "T"; // culturally safe long time pattern
            }

            dateText.Text = DateTime.Now.ToString(timeFormat);

            string dateFormat = Settings.DateFormat;
            if (string.IsNullOrEmpty(dateFormat))
            {
                dateFormat = "D"; // culturally safe long date pattern
            }

            dateText.ToolTip = DateTime.Now.ToString(dateFormat);
        }

        private void OpenTimeDateCPL(object sender, RoutedEventArgs e)
        {
            Shell.StartProcess("timedate.cpl");
        }
        #endregion

        #region Events
        public IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == appbarMessageId && appbarMessageId != -1)
            {
                switch ((NativeMethods.AppBarNotifications)wParam.ToInt32())
                {
                    case NativeMethods.AppBarNotifications.PosChanged:
                        // Reposition to the top of the screen.
                        AppBarHelper.ABSetPos(handle, this.ActualWidth, this.ActualHeight, AppBarHelper.ABEdge.ABE_TOP);
                        if (Startup.MenuBarShadowWindow != null)
                            Startup.MenuBarShadowWindow.SetPosition();
                        break;

                    case NativeMethods.AppBarNotifications.FullScreenApp:
                        if ((int)lParam == 1)
                        {
                            this.Topmost = false;
                            Shell.ShowWindowBottomMost(hwnd);

                            if (Settings.EnableTaskbar)
                            {
                                Startup.TaskbarWindow.Topmost = false;
                                Shell.ShowWindowBottomMost(Startup.TaskbarWindow.handle);
                            }
                        }
                        else
                        {
                            this.Topmost = true;
                            Shell.ShowWindowTopMost(hwnd);

                            if (Settings.EnableTaskbar)
                            {
                                Startup.TaskbarWindow.Topmost = true;
                                Shell.ShowWindowTopMost(Startup.TaskbarWindow.handle);
                            }
                        }

                        break;

                    case NativeMethods.AppBarNotifications.WindowArrange:
                        if ((int)lParam != 0)    // before
                            this.Visibility = Visibility.Collapsed;
                        else                         // after
                            this.Visibility = Visibility.Visible;

                        break;
                }
                handled = true;
            }
            else if (msg == NativeMethods.WM_ACTIVATE)
            {
                AppBarHelper.AppBarActivate(hwnd);
            }
            else if (msg == NativeMethods.WM_WINDOWPOSCHANGED)
            {
                AppBarHelper.AppBarWindowPosChanged(hwnd);
            }
            else if (msg == NativeMethods.WM_DISPLAYCHANGE)
            {
                setPosition(((uint)lParam & 0xffff), ((uint)lParam >> 16));
                handled = true;
            }

            return IntPtr.Zero;
        }

        private void setPosition(uint x, uint y)
        {
            int sWidth;
            int sHeight;
            // adjust size for dpi
            AppBarHelper.TransformFromPixels(x, y, out sWidth, out sHeight);
            this.Top = 0;
            this.Left = 0;
            this.Width = sWidth;
            if (Startup.MenuBarShadowWindow != null)
                Startup.MenuBarShadowWindow.SetPosition();
        }

        private void OnWindowInitialized(object sender, EventArgs e)
        {
            Visibility = Visibility.Visible;
        }

        private void Window_SourceInitialized(object sender, EventArgs e)
        {
            helper = new WindowInteropHelper(this);

            HwndSource source = HwndSource.FromHwnd(helper.Handle);
            source.AddHook(new HwndSourceHook(WndProc));

            handle = helper.Handle;

            appbarMessageId = AppBarHelper.RegisterBar(handle, this.ActualWidth, this.ActualHeight, AppBarHelper.ABEdge.ABE_TOP);
        }

        private void Window_LocationChanged(object sender, EventArgs e)
        {
            if (this.Top != 0)
            {
                this.Top = 0;
                if (Startup.MenuBarShadowWindow != null)
                    Startup.MenuBarShadowWindow.SetPosition();
            }
        }

        private void OnWindowLoaded(object sender, RoutedEventArgs e)
        {
            if (Settings.EnableSysTray == true)
            {
                SysTray.InitializeSystemTray();
            }
        }

        private void OnWindowClosing(object sender, CancelEventArgs e)
        {
            this.Height = 0;
            AppBarHelper.ResetWorkArea();
            AppBarHelper.RegisterBar(handle, this.ActualWidth, this.ActualHeight);

            SysTray.DestroySystemTray();

            if (Startup.IsCairoUserShell)
                Shell.StartProcess("explorer.exe");
        }
        #endregion

        #region Cairo menu items
        private void AboutCairo(object sender, RoutedEventArgs e)
        {
            System.Reflection.Assembly assembly = System.Reflection.Assembly.GetExecutingAssembly();
            FileVersionInfo fvi = FileVersionInfo.GetVersionInfo(assembly.Location);
            string version = fvi.FileVersion;

            CairoMessage.ShowAlert(
                "Version " + version + " - Pre-release"
                +"\n\nCopyright © 2007-" + DateTime.Now.Year.ToString() + " Cairo Development Team and community contributors.  All rights reserved.", "Cairo Desktop Environment", MessageBoxImage.None);
        }

        private void OpenLogoffBox(object sender, RoutedEventArgs e)
        {
            bool? LogoffChoice = CairoMessage.ShowOkCancel("You will lose all unsaved documents and be logged off.", "Are you sure you want to log off now?", "Resources/logoffIcon.png", "Log Off", "Cancel");
            if (LogoffChoice.HasValue && LogoffChoice.Value)
            {
                NativeMethods.Logoff();
            }
        }

        private void OpenRebootBox(object sender, RoutedEventArgs e)
        {
            bool? RebootChoice = CairoMessage.ShowOkCancel("You will lose all unsaved documents and your computer will restart.", "Are you sure you want to restart now?", "Resources/restartIcon.png", "Restart", "Cancel");
            if (RebootChoice.HasValue && RebootChoice.Value)
            {
                NativeMethods.Reboot();
            }
        }

        private void OpenShutDownBox(object sender, RoutedEventArgs e)
        {
            bool? ShutdownChoice = CairoMessage.ShowOkCancel("You will lose all unsaved documents and your computer will turn off.", "Are you sure you want to shut down now?", "Resources/shutdownIcon.png", "Shut Down", "Cancel");
            if (ShutdownChoice.HasValue && ShutdownChoice.Value)
            {
                NativeMethods.Shutdown();
            }
        }

        private void OpenRunWindow(object sender, RoutedEventArgs e)
        {
            Interop.Shell.ShowRunDialog();
        }

        private void OpenCloseCairoBox(object sender, RoutedEventArgs e)
        {
            bool? CloseCairoChoice = CairoMessage.ShowOkCancel("You will need to reboot or use the start menu shortcut in order to run Cairo again.", "Are you sure you want to exit Cairo?", "Resources/exitIcon.png", "Exit Cairo", "Cancel");
            if (CloseCairoChoice.HasValue && CloseCairoChoice.Value)
            {
                shutdown();
            }
        }

        private void OpenControlPanel(object sender, RoutedEventArgs e)
        {
            Shell.StartProcess("control.exe");
        }

        private void OpenTaskManager(object sender, RoutedEventArgs e)
        {
            Shell.StartProcess("taskmgr.exe");
        }

        private void SysSleep(object sender, RoutedEventArgs e)
        {
            NativeMethods.Sleep();
        }

        private void InitCairoSettingsWindow(object sender, RoutedEventArgs e)
        {
            CairoSettingsWindow window = new CairoSettingsWindow();
            window.Show();
        }

        private void InitAppGrabberWindow(object sender, RoutedEventArgs e)
        {
            appGrabber.ShowDialog();
        }
        #endregion

        #region Places menu items
        private void OpenMyDocs(object sender, RoutedEventArgs e)
        {
            Shell.StartProcess(fileManger, KnownFolders.GetPath(KnownFolder.Documents));
        }

        private void OpenMyPics(object sender, RoutedEventArgs e)
        {
            Shell.StartProcess(fileManger, KnownFolders.GetPath(KnownFolder.Pictures));
        }

        private void OpenMyMusic(object sender, RoutedEventArgs e)
        {
            Shell.StartProcess(fileManger, KnownFolders.GetPath(KnownFolder.Music));
        }

        private void OpenDownloads(object sender, RoutedEventArgs e)
        {
            Shell.StartProcess(fileManger, KnownFolders.GetPath(KnownFolder.Downloads));
        }

        private void OpenMyComputer(object sender, RoutedEventArgs e)
        {
            Shell.StartProcess(fileManger, "::{20D04FE0-3AEA-1069-A2D8-08002B30309D}");
        }

        private void OpenUserFolder(object sender, RoutedEventArgs e)
        {
            Shell.StartProcess(fileManger, System.Environment.GetEnvironmentVariable("USERPROFILE"));
        }

        private void OpenProgramFiles(object sender, RoutedEventArgs e)
        {
            Shell.StartProcess(fileManger, System.Environment.GetEnvironmentVariable("ProgramFiles"));
        }

        private void OpenRecycleBin(object sender, RoutedEventArgs e)
        {
            Shell.StartProcess(fileManger, "::{645FF040-5081-101B-9F08-00AA002F954E}");
        }
        #endregion

        #region Search menu
        private void btnViewResults_Click(object sender, RoutedEventArgs e)
        {
            Shell.StartProcess("search:query=" + searchStr.Text);
        }

        private void searchStr_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (searchStr.Text.Length > 0)
                btnViewResults.Visibility = Visibility.Visible;
            else
                btnViewResults.Visibility = Visibility.Collapsed;
        }

        private void searchStr_KeyDown(object sender, KeyEventArgs e)
        {
            if(e.Key == Key.Return)
            {
                Shell.StartProcess("search:query=" + searchStr.Text);
            }
        }

        public void FocusSearchBox(object sender, RoutedEventArgs e)
        {
            searchStr.Dispatcher.BeginInvoke(
            new Action(delegate
            {
                searchStr.Focusable = true;
                searchStr.Focus();
                Keyboard.Focus(searchStr);
            }),
            DispatcherPriority.Render);
        }

        public void ExecuteOpenSearchResult(object sender, ExecutedRoutedEventArgs e)
        {
            var searchObj = (VistaSearchProvider.SearchResult)e.Parameter;

            if (!Shell.StartProcess(searchObj.Path))
            {
                CairoMessage.Show("We were unable to open the search result.", "Uh Oh!", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        #endregion
    }
}
