using QuikSharp.DataStructures;
using QuikTester.Helpers;
using StockSharp.Algo.Indicators;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace QuikTester
{
    [DataContract]
    public enum StrategyType
    {
        [EnumMember]
        Ema,
        [EnumMember]
        Percent,
        [EnumMember]
        ValueDiff
    }

    [DataContract]
    public class PositionBot : Logger, INotifyPropertyChanged
    {
        private Operation prevDirection;

        private decimal _priceDeltaNow;

        /// <summary>
        /// высчитанный уровень в зависимости от направления 
        /// </summary>
        public decimal PriceDeltaNow { get; set; }


        private decimal _emanowLocalEma;

        /// <summary>
        /// EMA на текущий момент
        /// </summary>
        public decimal EmaNowLocalEma
        {
            get => _emanowLocalEma;
            set
            {
                _emanowLocalEma = value;
                PropertyEvent(nameof(EmaNowLocalEma));
            }
        }

        //[DataMember] 
        //public string SymbolWithPortfolio { get; set; }

        [DataMember]
        public string Symbol { get; set; }

        [DataMember]
        public string Portfolio { get; set; }


        private decimal _currentpos;

        public decimal CurrentPos
        {
            get => _currentpos;
            set
            {
                _currentpos = value;
                PropertyEvent(nameof(CurrentPos));
            }
        }

        private decimal _newPos;

        public decimal NewPos
        {
            get => _newPos;
            set
            {
                _newPos = value;
                PropertyEvent(nameof(NewPos));
            }
        }

        [DataMember] public bool Activated { get; set; }

        public QuikConnector QuikConnector { get; set; }

        [DataMember]
        public decimal ?PriceStep { get; set; }

        [DataMember]
        public string ?classCode { get; set; }

        private ExponentialMovingAverage EMA { get; set; }

        private StrategyType _strategyType;

        [DataMember]
        public StrategyType StrategyType
        {
            get => _strategyType;
            set
            {
                //cтратегия поменялась...
                if (_strategyType != value &&  QuikConnector != null)
                {
                    UpdateCalculationsAndPositions(NewPos);
                }

                _strategyType = value;
                
            }
        }

        private bool _started;

        public bool Started
        {
            get => _started;
            set
            {
                //поменялось состояние стратегии
                //  if (_started != value && value == true && QuikConnector!=null)
                // {
                //      UpdateCalculationsAndPositions(NewPos);
                //  }

                if (_started != value && value)
                {
                    MainAlgo();
                }

                if (_started != value && !value)
                {
                    //остановка 
                    TryToFindAndCancelStopOrder();
                }

                _started = value;
            }
        }

        public string StrategyTypeString
        {
            get => StrategyType.ToString();
            set
            {
                Enum.TryParse<StrategyType>(value, out var result);
                StrategyType = result;
            }
        }

        /*------- три типа стратегии и их свойства-----*/

        [DataMember] public decimal LastEmaValue { get; set; }
        public decimal LastPrice { get; private set; }


        private decimal _delta;

        /// <summary>
        /// Дельта для высчитывания в пунтках
        /// </summary>
        [DataMember]
        public decimal Delta
        {
            get => _delta;
            set
            {
                _delta = value;
                UpdateUsualDelta();
                PropertyEvent(nameof(Delta));
            }
        }

        private decimal _percent;

        /// <summary>
        /// Процентное значение
        /// указывается в реальных процентах
        /// </summary>
        [DataMember]
        public decimal Percent
        {
            get => _percent;
            set
            {
                _percent = value;

                UpdateDeltaPercent();

                PropertyEvent(nameof(Percent));

            }
        }


    /// <summary>
        /// Цена "отступа" в процентах или в реальном значении... зависит от настроек типа стратегии
        /// </summary>
        public decimal CurretPriceDelta { get;set;
           /* get
            {
                if (LastPrice == 0) return 0;
                if (Direction == Operation.Buy) return LastPrice - Delta;

                return LastPrice + Delta;
            }*/
        }



        /* -------------------------------------------*/


        [DataMember]
        public CandleInterval CandleInterval { get; set; }

        

        public string CandleIntervalString
        {
            get => CandleInterval.ToString();
            set
            {
                Enum.TryParse<CandleInterval>(value, out var result);

                if (result != CandleInterval)
                {
                    //поменялось значение и следовательно надо подписку остановить также... 

                }

                CandleInterval = result;
            }
        }

         private int _emaLength;


        [DataMember]
        public int EmaLength
        {
            get => _emaLength;
            set
            {
                if (value != _emaLength)
                {
                    //новое значение, значит обнуляем индикатор и строим заново... 
                    EMA = null;
                }

                _emaLength = value;
                PropertyEvent(nameof(EmaLength));
            }
        }

        /// <summary>
        /// По умолчанию ставлю на покупку направление
        /// todo потом сделаю динамчиеским 
        /// </summary>
        public Operation Direction { get; set; } = Operation.Buy;

        public PositionBot(QuikConnector quikConnector)
        {
            QuikConnector = quikConnector;
        }

        private void UpdateDeltaPercent()
        {
            if (LastPrice != 0)

                PriceDeltaNow = Math.Round(Direction == Operation.Buy
                    ? LastPrice * (1 - Percent / 100)
                    : (1 + Percent / 100), QuikConnector.getDecimalCount(LastPrice));
        }

        private void UpdateUsualDelta()
        {
            if (LastPrice != 0)
                PriceDeltaNow = Math.Round(Direction == Operation.Buy ? 
                    LastPrice - Delta : LastPrice + Delta, QuikConnector.getDecimalCount(LastPrice));
        }

        public Operation OppositeDirection => Direction == Operation.Buy ? Operation.Sell : Operation.Buy;
        public async void UpdateCalculationsAndPositions(decimal newPos)
        {

            //для быстрого обновления графики 
            NewPos = newPos;

            if (NewPos > 0) Direction = Operation.Buy;
            if (NewPos < 0) Direction = Operation.Sell;

            //приходится делать новый поток потому что получение свечек в квике все равно выполняется синхронно
            //из-за этого грузит графику.

            new Task(async () =>
            {

                GetInitialSettings();

                //----------- решил сделать калькуляцию параметров вне зависимости от того запущена или нет ----- 

                //  var signalEma = new ExponentialMovingAverage() { Length = _length };



                if (StrategyType == StrategyType.ValueDiff)
                {
                    LastPrice = await QuikConnector.GetEmaValueOrLastPrice(posbot: this, ema: false);

                    UpdateUsualDelta();

                   // LogMessage($" {SymbolWithPortfolio} Последняя цена {LastPrice} price Delta {PriceDeltaNow}");
                }

                if (StrategyType == StrategyType.Percent)
                {
                    LastPrice = await  QuikConnector.GetEmaValueOrLastPrice(posbot: this, ema: false);

                    UpdateDeltaPercent();

                   // LogMessage($" {SymbolWithPortfolio} Последняя цена {LastPrice} price Delta {PriceDeltaNow}");
                }

                if (StrategyType == StrategyType.Ema)
                {
                    EmaNowLocalEma =
                        Math.Round(await QuikConnector.GetEmaValueOrLastPrice(this, true, CandleInterval, EmaLength),
                            QuikConnector.DecimalsWithInstrument[Symbol]);

                    //LogMessage($"{SymbolWithPortfolio} Скользяшка {EmaNowLocalEma} ");
                }


            //--------------------------------------------------------------------------------------------------
            if (Activated && Started)
                MainAlgo();

                CurrentPos = NewPos;
                prevDirection = Direction;

           }).Start();
        }


        private void ReSubscribe( CandleInterval oldCandleInterval, CandleInterval newCandleInterval)
        {
            try
            {
                if (oldCandleInterval != null)
                    QuikConnector._quikconnector.Candles.Unsubscribe(classCode, Symbol, oldCandleInterval);
            }
            catch (Exception ex)
            {
                //на случай если произойдет не айс 
            }


            EMA = null;
            EMA = new ExponentialMovingAverage() { Length = EmaLength };




        }

        public void ProcessCandle()
        {

        }

        private void GetInitialSettings()
        {
            if (classCode == null)
            {
                classCode = QuikConnector.GetClassCodeForInsturment(Symbol);
                LogMessage($"Получен класс инструмента для {Symbol} -> {classCode}");
            }

            if (PriceStep == null)
            {
                PriceStep = QuikConnector.GetPriceStep(Symbol, classCode);
                LogMessage($"Получен шаг цены для {Symbol} -> {PriceStep}");
            }

           
        }


        private void MainAlgo()
        {

            new Task(async () =>
            {

                //заняты выставлением стопа в настоящий момент
                if(stopplacingprocess)
                    return;

                //Произошло закрытие позиции...
                if (NewPos == 0 && NewPos != CurrentPos)
                {
                    LogMessage("Позиция обнулилась. Отменяем стоп ордер ");
                    TryToFindAndCancelStopOrder();
                }

                if (NewPos != 0)
                {
                    var stoporder = QuikConnector
                        .ActiveStopOrders
                        .FirstOrDefault(s => s.Value.SecCode == Symbol && s.Value.ClientCode == Portfolio).Value;

                    if (StrategyType == StrategyType.Percent || StrategyType == StrategyType.ValueDiff)
                    {

                        /*
                        //сменилось направление или с самого нуля стартуем
                        if ((CurretPriceDelta == 0) || prevDirection != Direction)
                        {
                            LogMessage(
                                $"{Symbol} Первый стоп или изменение направления. Отменя и выставляем новый. Направление {Direction} цена = {PriceDeltaNow} ");
                            CurretPriceDelta = PriceDeltaNow;
                            CancelAndPlaceNewStopOrder(CurretPriceDelta, (int)NewPos, OppositeDirection,
                                QuikConnector.getDecimalCount(LastPrice), Portfolio);
                        }*/

                        //if (CurretPriceDelta != 0 && prevDirection == Direction || stoporder == null)
                        if(PriceDeltaNow!=0)
                        {
                            if ((Direction == Operation.Buy && PriceDeltaNow > CurretPriceDelta) ||
                                (Direction == Operation.Sell && PriceDeltaNow < CurretPriceDelta) || stoporder == null)
                            {
                                LogMessage(
                                    $"Выставляю стоп Новый ={PriceDeltaNow} Старый = {CurretPriceDelta}. Направление {Direction}");

                                CurretPriceDelta = PriceDeltaNow;

                                await CancelAndPlaceNewStopOrder(CurretPriceDelta, (int)NewPos, OppositeDirection,
                                    QuikConnector.getDecimalCount(LastPrice), Portfolio);
                            }
                        }

                    }
                    else
                    {
                        if (EmaNowLocalEma == 0)
                        {
                            LogMessage("Значение индикатора 0 ");
                            return;
                        }

                        /*
                        //сменилось направление или с самого нуля стартуем
                        if (LastEmaValue == 0 || prevDirection != Direction)
                        {
                            LogMessage(
                                $"{Symbol} Первый стоп или изменение направления. Отменя и выставляем новый. Направление {Direction} цена = {EmaNowLocalEma} ");
                            LastEmaValue = EmaNowLocalEma;
                            //todo - заменить нулевые значения. Добавить в основной коннектор количество чисел после запятой
                            CancelAndPlaceNewStopOrder(LastEmaValue, (int)NewPos, Direction,
                                QuikConnector.DecimalsWithInstrument[Symbol], Portfolio);
                        }*/

                       // if (LastEmaValue != 0 && prevDirection == Direction || stoporder == null)
                        {


                            if ((Direction == Operation.Buy && EmaNowLocalEma > LastEmaValue) ||
                                (Direction == Operation.Sell && LastEmaValue < LastEmaValue) || stoporder == null)
                            {
                                LogMessage(
                                    $"Выставляю. Новый ={EmaNowLocalEma} Старый = {LastEmaValue}. Направление {Direction}");
                                LastEmaValue = EmaNowLocalEma;

                               await CancelAndPlaceNewStopOrder(LastEmaValue, (int)NewPos, OppositeDirection,
                                    QuikConnector.DecimalsWithInstrument[Symbol], Portfolio);
                            }
                        }

                    }
                }
            }).Start();

        }

        private bool stopplacingprocess = false;
        // QuikConnector.PlaceStopOrder(PriceDelta, (int)newPos, Direction, SymbolWithPortfolio, QuikConnector.getDecimalCount(LastPrice));
        private async Task CancelAndPlaceNewStopOrder(decimal price, int quantity, Operation operation,int numberRound,string clientcode)
        {
            stopplacingprocess = true;

            TryToFindAndCancelStopOrder();
            await QuikConnector.PlaceStopOrder(price, quantity, operation, Symbol, numberRound, clientcode, 
                (decimal)PriceStep, classCode);

            stopplacingprocess = false;
        }
        private void TryToFindAndCancelStopOrder()
        {
            try
            {

                var stoporder = QuikConnector.ActiveStopOrders
                    .FirstOrDefault(s => s.Value.SecCode == Symbol && s.Value.ClientCode == Portfolio).Value;

                if (stoporder != null)
                    QuikConnector.CancelStopOrder(stoporder);
            }
            catch (Exception ex)
            {
                LogMessage(ex.Message);
            }

        }


        public void RefreshPosBot()
        {
            LastEmaValue = 0;
            LastPrice = 0;
         
        }

        public void Start()
        {
            if(Activated)
            Started = true;
        }
        public void Stop()
        {
            Started = false;
        }


        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
        public void PropertyEvent(string _property)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(_property));
        }

    }
}
