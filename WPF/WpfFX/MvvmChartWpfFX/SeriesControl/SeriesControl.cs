﻿using System;
using System.Collections;
using System.Collections.Specialized;
using System.Configuration;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Shapes;
using MvvmCharting.Common;
using MvvmCharting.Drawing;
using MvvmCharting.Series;
using MvvmCharting.WpfFX.Series;
using static System.Double;

namespace MvvmCharting.WpfFX.Series
{
    /// <summary>
    /// Represents a series control which can hold as many as four types of series:
    /// <see cref="LineSeries"/>, <see cref="AreaSeries"/>, <see cref="ScatterSeries"/>,
    /// <see cref="BarSeries"/>. They can be added, removed or hid dynamically.
    /// When overlapped one another, they together can compose a sophisticated series
    /// as a whole. 
    /// </summary>
    public class SeriesControl : InteractiveControl, ISeriesControl,
        IScatterSeriesOwner,
        ILineSeriesOwner,
        IBarSeriesOwner
    {
        private static IValueConverter B2v = new BooleanToVisibilityConverter();
        static SeriesControl()
        {
            DefaultStyleKeyProperty.OverrideMetadata(typeof(SeriesControl), new FrameworkPropertyMetadata(typeof(SeriesControl)));
        }

        public event Action<ISeriesControl, Range> XRangeChanged;
        public event Action<ISeriesControl, Range> YRangeChanged;

        public ISeriesControlOwner SeriesControlOwner=> this.Owner; 

        private ISeriesControlOwner _owner;
        internal ISeriesControlOwner Owner
        {
            get { return this._owner; }
            set
            {
                if (this._owner != value)
                {
                    this._owner = value;

                    //this.LineSeries?.UpdateShape();
                    //this.AreaSeries?.UpdateShape();
                    //this.ScatterSeries?.UpdateCoordinate();
                    //this.BarSeries?.UpdateBarWidth(this.MinXValueGap, this.XPixelPerUnit);
                    //this.BarSeries?.UpdateBarItemCoordinateAndHeight();
                }

            }
        }

        private ContentControl PART_LineSeriesHolder;
        private ContentControl PART_AreaSeriesHolder;
        private ContentControl PART_ScatterSeriesHolder;
        private ContentControl PART_BarSeriesHolder;

        public SeriesControl()
        {
            this.Loaded += SeriesHost_Loaded;

        }

        private void SeriesHost_Loaded(object sender, RoutedEventArgs e)
        {
            RecalculateCoordinate();
        }

        #region overrides
        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();

            this.PART_LineSeriesHolder = (ContentControl)this.GetTemplateChild("PART_LineSeriesHolder");
            if (this.PART_LineSeriesHolder != null)
            {

                this.PART_LineSeriesHolder.Content = this.LineSeries;
                this.PART_LineSeriesHolder.SetBinding(UIElement.VisibilityProperty,
                    new Binding(nameof(this.IsLineSeriesVisible)) { Source = this, Converter = B2v });
            }

            this.PART_AreaSeriesHolder = (ContentControl)this.GetTemplateChild("PART_AreaSeriesHolder");
            if (this.PART_AreaSeriesHolder != null)
            {
                this.PART_AreaSeriesHolder.Content = this.AreaSeries;
                this.PART_AreaSeriesHolder.SetBinding(UIElement.VisibilityProperty,
                    new Binding(nameof(this.IsAreaSeriesVisible)) { Source = this, Converter = B2v });
            }

            this.PART_ScatterSeriesHolder = (ContentControl)this.GetTemplateChild("PART_ScatterSeriesHolder");
            if (this.PART_ScatterSeriesHolder != null)
            {
                this.PART_ScatterSeriesHolder.Content = this.ScatterSeries;
                this.PART_ScatterSeriesHolder.SetBinding(UIElement.VisibilityProperty,
                    new Binding(nameof(this.IsScatterSeriesVisible)) { Source = this, Converter = B2v });
            }

            this.PART_BarSeriesHolder = (ContentControl)this.GetTemplateChild("PART_BarSeriesHolder");
            if (this.PART_BarSeriesHolder != null)
            {
                this.PART_BarSeriesHolder.Content = this.BarSeries;
                this.PART_BarSeriesHolder.SetBinding(UIElement.VisibilityProperty,
                    new Binding(nameof(this.IsBarSeriesVisible)) { Source = this, Converter = B2v });
            }
        }

        protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
        {
            base.OnRenderSizeChanged(sizeInfo);

            if (sizeInfo.NewSize.NearlyEqual(sizeInfo.PreviousSize))
            {
                return;
            }
           
            if (sizeInfo.WidthChanged)
            {
                UpdateXPixelPerUnit();
            }

            if (sizeInfo.HeightChanged)
            {
                UpdateYPixelPerUnit();
            }

            RecalculateCoordinate();
        }


        #endregion

        #region Validate data
        private void EnsureYValuePositive(IList list)
        {
            if (list == null)
            {
                return;
            }

            if (this.Owner.StackMode == StackMode.None)
            {
                return;
            }

            foreach (var item in list)
            {
                var y = GetYValueForItem(item);
                if (y < 0)
                {
                    throw new MvvmChartModelDataException($"Item value of {this.Owner.StackMode} series cannot be negative!");
                }
            }
        }

        internal void ValidateData()
        {
            EnsureYValuePositive(this.ItemsSource);

            if (!this.Owner.IsXAxisCategory && (this.Owner.StackMode == StackMode.None))
            {
                return;
            }

            var firstSh = this.Owner.GetSeries().FirstOrDefault(x => x != this && x.ItemsSource != null);

            if (firstSh == null)
            {
                return;
            }

            string strReason = this.Owner.StackMode != StackMode.None ? $"In {this.Owner.StackMode} mode" : "If the XAxis of a Chart is CategoryAxis";

            if (firstSh.ItemsSource.Count != this.ItemsSource.Count)
            {
                throw new MvvmChartModelDataException($"{strReason}, the item count of all series in a Chart must be same!");
            }

            for (int i = 0; i < this.ItemsSource.Count; i++)
            {
                if (this.Owner.IsXAxisCategory)
                {
                    var x1 = GetXRawValueForItem(this.ItemsSource[i]);
                    var x2 = GetXRawValueForItem(firstSh.ItemsSource[i]);
                    if (x1 == null || x2 == null)
                    {
                        throw new MvvmChartModelDataException("An Item's X value of the series ItemsSource cannot be null!");
                    }

                    if (!x1.Equals(x2))
                    {
                        throw new MvvmChartModelDataException($"{strReason}, the item's x value in the same index of all series in a Chart must be same!");
                    }
                }
                else
                {
                    var x1 = GetXValueForItem(this.ItemsSource[i]);
                    var x2 = GetXValueForItem(firstSh.ItemsSource[i]);

                    if (!x1.NearlyEqual(x2))
                    {
                        throw new MvvmChartModelDataException($"{strReason}, the item's x value in the same index of all series in a Chart must be same!");
                    }
                }

            }



        }

        #endregion

        #region IndependentValueProperty & DependentValueProperty properties
        public string IndependentValueProperty
        {
            get { return (string)GetValue(IndependentValuePropertyProperty); }
            set { SetValue(IndependentValuePropertyProperty, value); }
        }
        public static readonly DependencyProperty IndependentValuePropertyProperty =
            DependencyProperty.Register("IndependentValueProperty", typeof(string), typeof(SeriesControl), new PropertyMetadata(null));


        public string DependentValueProperty
        {
            get { return (string)GetValue(DependentValuePropertyProperty); }
            set { SetValue(DependentValuePropertyProperty, value); }
        }
        public static readonly DependencyProperty DependentValuePropertyProperty =
            DependencyProperty.Register("DependentValueProperty", typeof(string), typeof(SeriesControl), new PropertyMetadata(null));
        #endregion

        #region IsScatterSeriesVisible & IsLineSeriesVisible & IsAreaSeriesVisible properties
        public bool IsScatterSeriesVisible
        {
            get { return (bool)GetValue(IsScatterSeriesVisibleProperty); }
            set { SetValue(IsScatterSeriesVisibleProperty, value); }
        }
        public static readonly DependencyProperty IsScatterSeriesVisibleProperty =
            DependencyProperty.Register("IsScatterSeriesVisible", typeof(bool), typeof(SeriesControl), new PropertyMetadata(true));

        public bool IsLineSeriesVisible
        {
            get { return (bool)GetValue(IsLineSeriesVisibleProperty); }
            set { SetValue(IsLineSeriesVisibleProperty, value); }
        }
        public static readonly DependencyProperty IsLineSeriesVisibleProperty =
            DependencyProperty.Register("IsLineSeriesVisible", typeof(bool), typeof(SeriesControl), new PropertyMetadata(true));

        public bool IsAreaSeriesVisible
        {
            get { return (bool)GetValue(IsAreaSeriesVisibleProperty); }
            set { SetValue(IsAreaSeriesVisibleProperty, value); }
        }
        public static readonly DependencyProperty IsAreaSeriesVisibleProperty =
            DependencyProperty.Register("IsAreaSeriesVisible", typeof(bool), typeof(SeriesControl), new PropertyMetadata(true));

        public bool IsBarSeriesVisible
        {
            get { return (bool)GetValue(IsBarSeriesVisibleProperty); }
            set { SetValue(IsBarSeriesVisibleProperty, value); }
        }
        public static readonly DependencyProperty IsBarSeriesVisibleProperty =
            DependencyProperty.Register("IsBarSeriesVisible", typeof(bool), typeof(SeriesControl), new PropertyMetadata(true));



        #endregion

        

        #region ItemsSource property and handlers


        /// <summary>
        /// Represents the data for a series.
        /// Currently can only handle numerical(& DateTime, DataTimeOffset) data.
        /// NOTE: It will be the user's responsibility to keep the ItemsSource sorted
        /// by Independent property(x value) ascendingly! 
        /// </summary>
        public IList ItemsSource
        {
            get { return (IList)GetValue(ItemsSourceProperty); }
            set { SetValue(ItemsSourceProperty, value); }
        }
        public static readonly DependencyProperty ItemsSourceProperty =
            DependencyProperty.Register("ItemsSource", typeof(IList), typeof(SeriesControl), new PropertyMetadata(null, OnItemsSourcePropertyChanged));

        private static void OnItemsSourcePropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            SeriesControl c = (SeriesControl)d;

            c.OnItemsSourceChanged((IList)e.OldValue, (IList)e.NewValue);

        }
 
        private void OnItemsSourceChanged(IList oldValue, IList newValue)
        {
            ValidateData();

            if (oldValue is INotifyCollectionChanged oldItemsSource)
            {
                WeakEventManager<INotifyCollectionChanged, NotifyCollectionChangedEventArgs>
                    .RemoveHandler(oldItemsSource, "CollectionChanged", ItemsSource_CollectionChanged);
            }

            if (newValue != null)
            {
                if (newValue is INotifyCollectionChanged newItemsSource)
                {
                    WeakEventManager<INotifyCollectionChanged, NotifyCollectionChangedEventArgs>
                        .AddHandler(newItemsSource, "CollectionChanged", ItemsSource_CollectionChanged);
                }
            }

            Reset();

            this.ScatterSeries?.UpdateItemsSource();

            this.BarSeries?.UpdateItemsSource();

            Refresh();

        }

        private void ItemsSource_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action == NotifyCollectionChangedAction.Move)
            {
                return;
            }

            if (!this.Owner.IsSeriesCollectionChanging)
            {
                /*                if (this.Owner.IsXAxisCategory)
                                {
                                    throw new MvvmChartModelDataException($"Collection change of ItemsSource is not allowed when XAxis is CategoryAxis and {nameof(this.Owner.IsSeriesCollectionChanging)} is false!");
                                }*/

                if (this.Owner.StackMode != StackMode.None)
                {
                    throw new MvvmChartModelDataException($"Collection change of ItemsSource is not allowed in {this.Owner.StackMode} mode when {nameof(this.Owner.IsSeriesCollectionChanging)} is false!");
                }
            }

            HandleItemsSourceCollectionChange(e.OldItems, e.NewItems);
        }

        private double _minXValueGap = NaN;
        public double MinXValueGap
        {
            get { return this._minXValueGap; }
            set
            {
                if (this._minXValueGap != value)
                {
                    this._minXValueGap = value;

                    this.BarSeries?.UpdateBarWidth(this.MinXValueGap, this.XPixelPerUnit);
                }
            }
        }
        private void UpdateMinXValueGap()
        {
            if (this.Owner.IsXAxisCategory)
            {
                this.MinXValueGap = 1.0;
                this.BarSeries?.UpdateBarWidth(this.MinXValueGap, this.XPixelPerUnit);
                return;
            }

            double prev = NaN;
            this.MinXValueGap = MaxValue;
            foreach (var item in this.ItemsSource)
            {
                if (prev.IsNaN())
                {

                    prev = GetXValueForItem(item);
                    continue;
                }

                var current = GetXValueForItem(item);
                var gap = current - prev;
                if (gap < this.MinXValueGap)
                {
                    this.MinXValueGap = gap;
                }
            }
        }

        private void HandleItemsSourceCollectionChange(IList oldValue, IList newValue)
        {
            Reset();
            EnsureYValuePositive(newValue);
            UpdateMinXValueGap();
            UpdateValueRange();
            RecalculateCoordinate();
  
        }

        private double GetXValueForItem(object item)
        {
            var t = item.GetType();
            var x = t.GetProperty(this.IndependentValueProperty).GetValue(item);

            return DoubleValueConverter.ObjectToDouble(x);

        }

        internal object GetXRawValueForItem(object item)
        {
            var t = item.GetType();
            var x = t.GetProperty(this.IndependentValueProperty).GetValue(item);

            return x;

        }

        private double GetYValueForItem(object item)
        {
            var t = item.GetType();
            var yProp = t.GetProperty(this.DependentValueProperty);
            var y = yProp.GetValue(item);

            return DoubleValueConverter.ObjectToDouble(y);

        }

        private double GetAdjustYValueForItem(object item, int index)
        {
            switch (this.Owner.StackMode)
            {
                case StackMode.None:

                    return GetYValueForItem(item);

                case StackMode.Stacked:
                    double total = 0;
                    foreach (var sr in this.Owner.GetSeries())
                    {
                        var obj = sr.ItemsSource[index];
                        var y = sr.GetYValueForItem(obj);
                        total += y;
                        if (obj == item)
                        {
                            break;
                        }


                    }

                    return total;

                case StackMode.Stacked100:
                    bool meet = false;
                    total = 0;
                    double sum = 0;
                    foreach (var sr in this.Owner.GetSeries())
                    {
                        var obj = sr.ItemsSource[index];
                        total += sr.GetYValueForItem(obj);

                        if (!meet)
                        {
                            sum = total;
                        }

                        if (obj == item)
                        {
                            meet = true;
                        }

                    }

                    return sum / total;
                default:
                    throw new ArgumentOutOfRangeException();
            }




        }

        public void UpdateValueRange()
        {
            if (this.Owner.IsSeriesCollectionChanging)
            {
                this.XValueRange = Range.Empty;
                this.YValueRange = Range.Empty;
                return;
            }

            if (this.ItemsSource == null ||
                this.ItemsSource.Count == 0)
            {
                this.XValueRange = Range.Empty;
                this.YValueRange = Range.Empty;

                return;
            }


 
            double minY = MaxValue;
            double maxY = MinValue;
            double minX = MaxValue;
            double maxX = MinValue;
            for (int i = 0; i < this.ItemsSource.Count; i++)
            {
                var item = this.ItemsSource[i];

                var y = GetAdjustYValueForItem(item, i);

                minY = Math.Min(minY, y);
                maxY = Math.Max(maxY, y);

                if (!this.Owner.IsXAxisCategory)
                {
                    var x = GetXValueForItem(item);

                    minX = Math.Min(minX, x);
                    maxX = Math.Max(maxX, x);
                }
            }


            if (this.Owner.IsXAxisCategory)
            {
                minX = 0;
                maxX = this.ItemsSource.Count;
            }

            this.XValueRange = new Range(minX, maxX);
            this.YValueRange = new Range(minY, maxY);

        }

        #endregion

        #region value range
        private Range _yValueRange = Range.Empty;
        /// <summary>
        /// The min & max of the dependent value
        /// </summary>
        public Range YValueRange
        {
            get { return this._yValueRange; }
            private set
            {
                if (this._yValueRange != value)
                {
                    this._yValueRange = value;

 
                    this.YRangeChanged?.Invoke(this, this.YValueRange);

                }

            }
        }

        private Range _xValueRange = Range.Empty;
        /// <summary>
        /// The min & max of the dependent value
        /// </summary>
        public Range XValueRange
        {
            get { return this._xValueRange; }
            private set
            {
                if (this._xValueRange != value)
                {
                    this._xValueRange = value;
                    this.XRangeChanged?.Invoke(this, this.XValueRange);


                }
            }
        }
        #endregion

        #region plotting value range
        private Range _plottingXValueRange = Range.Empty;
        /// <summary>
        /// The final X value range used to plot the chart
        /// </summary>
        protected Range PlottingXValueRange
        {
            get { return this._plottingXValueRange; }
            set
            {
                if (!this._plottingXValueRange.Equals(value) )
                {
                    this._plottingXValueRange = value;
                    UpdateXPixelPerUnit();
                    RecalculateCoordinate();
                }
            }
        }

        private Range _valuePlottingYValueRange = Range.Empty;
        /// <summary>
        /// The final Y value range used to plot the chart
        /// </summary>
        protected Range PlottingYValueRange
        {
            get { return this._valuePlottingYValueRange; }
            set
            {
                if (!this._valuePlottingYValueRange.Equals(value) )
                {
                    this._valuePlottingYValueRange = value;
                    UpdateYPixelPerUnit();
                    RecalculateCoordinate();
                }
            }
        }

        public virtual void OnPlottingXValueRangeChanged(Range newValue)
        {
            this.PlottingXValueRange = newValue;
        }

        public virtual void OnPlottingYValueRangeChanged(Range newValue)
        {
            this.PlottingYValueRange = newValue;
        }
        #endregion

        #region Coordinates calculating
        private double _xPixelPerUnit;
        public double XPixelPerUnit
        {
            get { return this._xPixelPerUnit; }
            set
            {
                if (this._xPixelPerUnit != value)
                {
                    this._xPixelPerUnit = value;
                    this.BarSeries?.UpdateBarWidth(this.MinXValueGap, this.XPixelPerUnit);
 
                }

            }
        }

        private double _yPixelPerUnit;
        public double YPixelPerUnit
        {
            get { return this._yPixelPerUnit; }
            set
            {
                if (this._yPixelPerUnit != value)
                {
                    this._yPixelPerUnit = value;
                }
            }
        }



        private void UpdateXPixelPerUnit()
        {
            if (this.PlottingXValueRange.IsEmpty ||
                this.RenderSize.IsInvalid())
            {
                this.XPixelPerUnit = NaN;
                return;
            }

            this.XPixelPerUnit = this.ActualWidth / this.PlottingXValueRange.Span;
        }

        private void UpdateYPixelPerUnit()
        {
            if (this.PlottingYValueRange.IsEmpty ||
                this.RenderSize.IsInvalid())
            {
                this.YPixelPerUnit = NaN;

                return;
            }

            this.YPixelPerUnit = this.ActualHeight / this.PlottingYValueRange.Span;

        }

        public PointNS GetPlotCoordinateForItem(object item, int itemIndex)
        {
            double x;

            if (!this.Owner.IsXAxisCategory)
            {
                x = GetXValueForItem(item);

            }
            else
            {
                x = itemIndex + 0.5;
            }

            var y = GetAdjustYValueForItem(item, itemIndex);

            var pt = new PointNS((x - this.PlottingXValueRange.Min) * this.XPixelPerUnit,
                (y - this.PlottingYValueRange.Min) * this.YPixelPerUnit);


            if (pt.Y < 0)
            {
                throw new NotImplementedException(this.DataContext.ToString());
            }

            return pt;
        }

        /// <summary>
        /// This should be call when: 1) ItemsSource; 2) x or y PixelPerUnit, and 3) RenderSize changed.
        /// This method will first update the _coordinateCache and the Coordinate of each scatter,
        /// then update the shape of the Line or Area
        /// </summary>
        internal void RecalculateCoordinate()
        {
            if (this.Owner.IsSeriesCollectionChanging)
            {
                return;
            }

            UpdateXPixelPerUnit();
            UpdateYPixelPerUnit();

            if (this.XPixelPerUnit.IsNaN() ||
                this.YPixelPerUnit.IsNaN() ||
                !this.IsLoaded)
            {
                return;
            }


            Array.Resize(ref this._coordinateCache, this.ItemsSource.Count);
            var previous = this.GetPreviousSeriesCoordinates(false);
            for (int i = 0; i < this.ItemsSource.Count; i++)
            {
                var item = this.ItemsSource[i];

                var pt = GetPlotCoordinateForItem(item, i);

                this._coordinateCache[i] = pt;

                this.ScatterSeries?.UpdateScatterCoordinate(item, pt);

                double y = previous?[i].Y ?? 0;
                this.BarSeries?.UpdateBarCoordinateAndHeight(item, pt, y);
            }

            this.LineSeries?.UpdateShape();
            this.AreaSeries?.UpdateShape();

            this.BarSeries?.UpdateBarWidth(this.MinXValueGap, this.XPixelPerUnit);

        
        }

        internal void Reset()
        {
            this.XPixelPerUnit = double.NaN;
            this.YPixelPerUnit = double.NaN;

            this.XValueRange = Range.Empty;
            this.YValueRange = Range.Empty;
        }

        internal void Refresh()
        {
            UpdateValueRange();

            RecalculateCoordinate();

        }

        /// <summary>
        /// cache the coordinate for performance
        /// </summary>
        private PointNS[] _coordinateCache;

        public PointNS[] GetCoordinates()
        {
            return this._coordinateCache;
        }


        private ISeriesControl GetPreviousSeriesHost()
        {
            return this.Owner.GetPreviousSeriesHost(this);
        }


        public PointNS[] GetPreviousSeriesCoordinates(bool isAreaSeries)
        {
            var coordinates = GetCoordinates();

            PointNS[] previous = null;

            switch (this.Owner.StackMode)
            {
                case StackMode.None:
                    if (isAreaSeries)
                    {
                        previous = new[] { new PointNS(coordinates.First().X, 0), new PointNS(coordinates.Last().X, 0) };
                    }
                    break;
                case StackMode.Stacked:
                case StackMode.Stacked100:
                    ISeriesControl previousSeriesControl = GetPreviousSeriesHost();
                    if (previousSeriesControl == null)
                    {
                        if (isAreaSeries)
                        {
                            previous = new[] { new PointNS(coordinates.First().X, 0), new PointNS(coordinates.Last().X, 0) };
                        }

                    }
                    else
                    {
                        previous = previousSeriesControl.GetCoordinates();

                        if (previous.Length != coordinates.Length)
                        {
                            throw new MvvmChartException($"previous.Length({previous.Length}) != coordinates.Length({coordinates.Length})");
                        }
                    }

                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }


            return previous;
        }


        #endregion

        #region series
        public LineSeries LineSeries
        {
            get { return (LineSeries)GetValue(LineSeriesProperty); }
            set { SetValue(LineSeriesProperty, value); }
        }
        public static readonly DependencyProperty LineSeriesProperty =
            DependencyProperty.Register("LineSeries", typeof(LineSeries), typeof(SeriesControl), new PropertyMetadata(null, OnLineSeriesPropertyChanged));

        private static void OnLineSeriesPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            ((SeriesControl)d).OnLineSeriesChanged();
        }

        private void OnLineSeriesChanged()
        {
            if (this.PART_LineSeriesHolder != null)
            {
                this.PART_LineSeriesHolder.Content = this.LineSeries;
            }

            if (this.LineSeries != null)
            {
                this.LineSeries.Owner = this;
                this.LineSeries.UpdateShape();
            }

        }

        public AreaSeries AreaSeries
        {
            get { return (AreaSeries)GetValue(AreaSeriesProperty); }
            set { SetValue(AreaSeriesProperty, value); }
        }
        public static readonly DependencyProperty AreaSeriesProperty =
            DependencyProperty.Register("AreaSeries", typeof(AreaSeries), typeof(SeriesControl), new PropertyMetadata(null, OnAreaSeriesPropertyChanged));

        private static void OnAreaSeriesPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            ((SeriesControl)d).OnAreaSeriesChanged();
        }

        private void OnAreaSeriesChanged()
        {
            if (this.PART_AreaSeriesHolder != null)
            {
                this.PART_AreaSeriesHolder.Content = this.AreaSeries;
            }

            if (this.AreaSeries != null)
            {
                this.AreaSeries.Owner = this;
                this.AreaSeries.UpdateShape();
            }
        }

        public ScatterSeries ScatterSeries
        {
            get { return (ScatterSeries)GetValue(ScatterSeriesProperty); }
            set { SetValue(ScatterSeriesProperty, value); }
        }
        public static readonly DependencyProperty ScatterSeriesProperty =
            DependencyProperty.Register("ScatterSeries", typeof(ScatterSeries), typeof(SeriesControl), new PropertyMetadata(null, OnScatterSeriesPropertyChanged));

        private static void OnScatterSeriesPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            ((SeriesControl)d).OnScatterSeriesChanged();
        }

        private void OnScatterSeriesChanged()
        {
            if (this.PART_ScatterSeriesHolder != null)
            {
                this.PART_ScatterSeriesHolder.Content = this.ScatterSeries;
            }

            if (this.ScatterSeries != null)
            {
                this.ScatterSeries.Owner = this;
                this.ScatterSeries.UpdateItemsSource();
            }
        }


        public BarSeries BarSeries
        {
            get { return (BarSeries)GetValue(BarSeriesProperty); }
            set { SetValue(BarSeriesProperty, value); }
        }
        public static readonly DependencyProperty BarSeriesProperty =
            DependencyProperty.Register("BarSeries", typeof(BarSeries), typeof(SeriesControl), new PropertyMetadata(null, OnBarSeriesPropertyChanged));

        private static void OnBarSeriesPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            ((SeriesControl)d).OnBarSeriesChanged();
        }
        private void OnBarSeriesChanged()
        {
            if (this.PART_BarSeriesHolder != null)
            {

                this.PART_BarSeriesHolder.Content = this.BarSeries;
            }

            if (this.BarSeries != null)
            {
                this.BarSeries.Owner = this;
                this.BarSeries.UpdateBarWidth(this.MinXValueGap, this.XPixelPerUnit);
                this.BarSeries.UpdateItemsSource();
            }
        }
        #endregion


    }



}
