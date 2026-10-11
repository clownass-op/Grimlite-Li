using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace Grimoire.Tools.Wiki
{
    public class DragAdorner : Adorner
    {
        private readonly Rectangle _visual;
        private double _left;
        private double _top;

        public DragAdorner(UIElement adornedElement, UIElement snapshotSource, double width, double height)
            : base(adornedElement)
        {
            IsHitTestVisible = false;

            var brush = new VisualBrush(snapshotSource)
            {
                Stretch = Stretch.None,
                AlignmentX = AlignmentX.Left,
                AlignmentY = AlignmentY.Top
            };

            _visual = new Rectangle
            {
                Width = width,
                Height = height,
                Fill = brush,
                Opacity = 0.92,
                RadiusX = 6,
                RadiusY = 6,
                IsHitTestVisible = false,
                Effect = new DropShadowEffect
                {
                    Color = Colors.Black,
                    BlurRadius = 18,
                    ShadowDepth = 4,
                    Opacity = 0.55
                },
                RenderTransform = new ScaleTransform(1.04, 1.04),
                RenderTransformOrigin = new Point(0.5, 0.5)
            };
        }

        public void UpdatePosition(double left, double top)
        {
            _left = left;
            _top = top;
            (Parent as AdornerLayer)?.Update(AdornedElement);
        }

        protected override Size MeasureOverride(Size constraint)
        {
            _visual.Measure(constraint);
            return _visual.DesiredSize;
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            _visual.Arrange(new Rect(finalSize));
            return finalSize;
        }

        protected override Visual GetVisualChild(int index) => _visual;
        protected override int VisualChildrenCount => 1;

        public override GeneralTransform GetDesiredTransform(GeneralTransform transform)
        {
            var group = new GeneralTransformGroup();
            group.Children.Add(base.GetDesiredTransform(transform));
            group.Children.Add(new TranslateTransform(_left, _top));
            return group;
        }
    }
}
