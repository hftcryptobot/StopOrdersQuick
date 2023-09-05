using QuikSharp.DataStructures;
using QuikTester.Helpers;
using System;
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

        [DataMember] public string Symbol { get; set; }

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
                if (_started != value && value == true && QuikConnector!=null)
                {
                    UpdateCalculationsAndPositions(NewPos);
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
                CandleInterval = result;
            }
        }
        
        [DataMember]
        public int EmaLength { get; set; }


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
                
                //----------- решил сделать калькуляцию параметров вне зависимости от того запущена или нет ----- 

                if (StrategyType == StrategyType.ValueDiff)
                {
                    LastPrice = await QuikConnector.GetEmaValueOrLastPrice(posbot: this, ema: false);

                    UpdateUsualDelta();

                    LogMessage($" {Symbol} Последняя цена {LastPrice} price Delta {PriceDeltaNow}");
                }

                if (StrategyType == StrategyType.Percent)
                {
                    LastPrice = await  QuikConnector.GetEmaValueOrLastPrice(posbot: this, ema: false);

                    UpdateDeltaPercent();

                    LogMessage($" {Symbol} Последняя цена {LastPrice} price Delta {PriceDeltaNow}");
                }

                if (StrategyType == StrategyType.Ema)
                {
                    EmaNowLocalEma =
                        Math.Round(await QuikConnector.GetEmaValueOrLastPrice(this, true, CandleInterval, EmaLength),
                            QuikConnector.DecimalsWithInstrument[Symbol]);

                    LogMessage($"{Symbol} Скользяшка {EmaNowLocalEma} ");
                }


                //--------------------------------------------------------------------------------------------------

                if (Activated && Started)
                {

                    //Произошло закрытие позиции...
                    if (NewPos == 0 && NewPos != CurrentPos)
                    {
                        LogMessage("Позиция обнулилась. Отменяем стоп ордер ");
                        TryToFindAndCancelStopOrder();
                    }

                    if (NewPos != 0)
                    {

                        if (StrategyType == StrategyType.Percent || StrategyType == StrategyType.ValueDiff)
                        {
                            

                            //сменилось направление или с самого нуля стартуем
                            if ((CurretPriceDelta == 0) || prevDirection != Direction)
                            {
                                LogMessage(
                                    $"{Symbol} Первый стоп или изменение направления. Отменя и выставляем новый. Направление {Direction} цена = {PriceDeltaNow} ");
                                CurretPriceDelta = PriceDeltaNow;
                                CancelAndPlaceNewStopOrder(CurretPriceDelta, (int)NewPos, OppositeDirection,
                                    QuikConnector.getDecimalCount(LastPrice));
                            }

                            if (CurretPriceDelta != 0 && prevDirection == Direction)
                            {
                                if ((Direction == Operation.Buy && PriceDeltaNow > CurretPriceDelta) ||
                                    (Direction == Operation.Sell && PriceDeltaNow < CurretPriceDelta))
                                {
                                    LogMessage(
                                        $"Уровень изменился. Новый ={PriceDeltaNow} Старый = {CurretPriceDelta}. Направление {Direction}");

                                    CurretPriceDelta = PriceDeltaNow;

                                    CancelAndPlaceNewStopOrder(CurretPriceDelta, (int)NewPos, OppositeDirection,
                                        QuikConnector.getDecimalCount(LastPrice));
                                }
                            }

                        }
                        else
                        {
                            //сменилось направление или с самого нуля стартуем
                            if (LastEmaValue == 0 || prevDirection != Direction)
                            {
                                LogMessage(
                                    $"{Symbol} Первый стоп или изменение направления. Отменя и выставляем новый. Направление {Direction} цена = {EmaNowLocalEma} ");
                                LastEmaValue = EmaNowLocalEma;
                                //todo - заменить нулевые значения. Добавить в основной коннектор количество чисел после запятой
                                CancelAndPlaceNewStopOrder(LastEmaValue, (int)NewPos, Direction, QuikConnector.DecimalsWithInstrument[Symbol]);
                            }

                            if (LastEmaValue != 0 && prevDirection == Direction)
                            {
                                if ((Direction == Operation.Buy && EmaNowLocalEma > LastEmaValue) ||
                                    (Direction == Operation.Sell && LastEmaValue < LastEmaValue))
                                {
                                    LogMessage(
                                        $"Уровень EMA изменился. Новый ={EmaNowLocalEma} Старый = {LastEmaValue}. Направление {Direction}");
                                    LastEmaValue = EmaNowLocalEma;

                                    CancelAndPlaceNewStopOrder(LastEmaValue, (int)NewPos, OppositeDirection,
                                        QuikConnector.DecimalsWithInstrument[Symbol]);
                                }
                            }

                        }
                    }
                }

                CurrentPos = NewPos;
                prevDirection = Direction;

           }).Start();
        }

        // QuikConnector.PlaceStopOrder(PriceDelta, (int)newPos, Direction, Symbol, QuikConnector.getDecimalCount(LastPrice));
        private void CancelAndPlaceNewStopOrder(decimal price, int quantity, Operation operation,int numberRound)
        {
            TryToFindAndCancelStopOrder();
            QuikConnector.PlaceStopOrder(price, quantity, operation, Symbol, numberRound);
        }
        private void TryToFindAndCancelStopOrder()
        {
            var stoporder = QuikConnector.ActiveStopOrders.FirstOrDefault(s => s.Value.SecCode == Symbol).Value;

            if (stoporder != null)
                QuikConnector.CancelStopOrder(stoporder);

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
