using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Timers;
using System.Windows;
using System.Windows.Media;
using DevExpress.Xpf.Editors.Themes;
using Ecng.ComponentModel;
using QuikSharp;
using QuikSharp.DataStructures;
using QuikTester.Helpers;
using StockSharp.Algo.Candles;
using StockSharp.Algo.Indicators;


//todo сделать динамическое изменени колонок в зависимости от типа стратегии

namespace QuikTester
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : DevExpress.Xpf.Core.ThemedWindow
    {
         ObservableCollection<PositionBot> _positionBots = new ObservableCollection<PositionBot>();

        // private ExponentialMovingAverage signallma;
        //private string _classcode = "TQBR";
        //private string _securityCode = "LKOH";


        private readonly object _objectlogger = new object();
        string _prevLogmessage = "";
        private readonly StreamWriter _logger = new StreamWriter(DateTime.Now.ToString("dd_MM_yyyy") + ".txt", true);

        QuikConnector QuikConnector { get; set; }

        private void SetUISettings()
        {
            StrategyTypComboBoxEditSettings.ItemsSource = Enum.GetNames(typeof(StrategyType));
            CandlesTypesComboBoxEditSettings.ItemsSource = Enum.GetNames(typeof(CandleInterval));
        }

        public MainWindow()
        {
            InitializeComponent();


            SetUISettings();

            QuikConnector = new QuikConnector()
            {
                Account = "L01+00000F00"
            };

            if (File.Exists(Helper.SettingsFile))
            {
                _positionBots = Helper.ReadXml<ObservableCollection<PositionBot>>();
            }

            BotPositionsGrid.ItemsSource = _positionBots;

            var timer = new System.Timers.Timer(1000);
            timer.Elapsed += (s, e) =>
            {
                Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                {

                    BotPositionsGrid.RefreshData();
                }));
            };
            timer.Start();

            QuikConnector.PositionsUpdate += (positions) =>
            {

                //создаем список ботов исходя из позиций.. 
                foreach (var pos in positions)
                {
                    var bot = _positionBots.FirstOrDefault(p => p.Symbol == pos.Key);
                    if (bot == null)
                    {
                        bot = new PositionBot(QuikConnector)
                        {
                            Symbol = pos.Key,
                            Activated = false,

                            CandleInterval = CandleInterval.H1,
                            EmaLength = 10,
                        };
                        bot.LogAction += LogMessage;

                        Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                        {
                            //BotPositionsGrid.RefreshData();
                            _positionBots.Add(bot);
                        }));

                    }
                    else
                    {
                        if (bot.QuikConnector == null)
                            bot.QuikConnector = QuikConnector;

                        if (bot.LogAction == null)
                            bot.LogAction += LogMessage;


                    }

                    Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        bot.UpdateCalculationsAndPositions(pos.Value);

                    }));
                }

                Helper.SaveXml(_positionBots);

            };
            QuikConnector.LogAction += LogMessage;

            new Thread(() => { QuikConnector.Connect(); }).Start();

        }


        protected override void OnClosing(CancelEventArgs e)
        {
            //сохраняем настройки перед закрытием принудительно
            Helper.SaveXml(_positionBots);

            LogMessage("Отключаемся от квика");
            if (QuikConnector == null)
            {
                base.OnClosing(e);
                return;
            }

            try
            {
                QuikConnector.Disconnected += () => 
                {
                    base.OnClosing(e);
                };
                QuikConnector.Stop();
            }
            catch (Exception ex)
            {
                base.OnClosing(e);
            }
           
        }


        
        public async void LogMessage(string message)
        {

            try
            {

                if (message == _prevLogmessage) return;

                var dt = DateTime.Now;
                var datetime = dt.ToString("H:mm:ss.fff");
                var logmessage = datetime + " | " + message;
                _prevLogmessage = message;

                //Debug.WriteLine(message);

                if (Application.Current != null)
                    Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        LogTextBox.AppendText(logmessage + Environment.NewLine);
                        LogTextBox.ScrollToEnd();

                    }));

                lock (_objectlogger)
                {
                    _logger.WriteLine(logmessage);
                }

                // logger.Close();
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.Message);
            }

        }

        bool active = false;
        private void StartCheck_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!active)
                {
                    active = true;

                    StartStopAll.Content = "Стоп";
                    StartStopAll.Foreground = new BrushConverter().ConvertFromString("#FFFFBEBE") as SolidColorBrush;

                    foreach (var bot in _positionBots)
                    {
                        bot.Start();
                    }
                }
                else
                {
                    active = false;

                    foreach (var bot in _positionBots)
                    {
                        bot.Stop();
                    }

                    StartStopAll.Content = "Старт";
                    StartStopAll.Foreground = new BrushConverter().ConvertFromString("#a2e83e") as SolidColorBrush;
                }
            }
            catch (Exception ex)
            {
                LogMessage(ex.Message);
            }
        }

        private void RefreshButton(object sender, RoutedEventArgs e)
        {
            var bot = (PositionBot)BotPositionsGrid.SelectedItem;

            if(bot!=null)
                bot.RefreshPosBot();
        }
    }
}
