using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.Storage;
using Windows.UI.Core.Preview;
using Windows.UI.Input.Inking;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;
using Windows.UI.Xaml.Shapes;

// The Blank Page item template is documented at https://go.microsoft.com/fwlink/?LinkId=402352&clcid=0x409

namespace Sketchblock
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    /// 
    public sealed partial class MainPage : Page
    {

        public Stack<InkStroke> UndoStrokes { get; set; }

        private Polyline lasso;
        private Rect boundingRect;
        private bool isSelecting;
        private bool isMooving;
        private Point lastPoint;

        private bool alreadySaved = false;
        private StorageFile lastFile; 

        Symbol TouchWritingIcon = (Symbol)0xED5F;
        Symbol SelectIcon = (Symbol)0xEF20;


        public MainPage()
        {
            this.InitializeComponent();
            UndoStrokes = new Stack<InkStroke>();
            canvas.InkPresenter.StrokesErased += Stroke_Erased;
            canvas.InkPresenter.StrokeInput.StrokeStarted += Stroke_Started;

            SystemNavigationManagerPreview.GetForCurrentView().CloseRequested += Close_Requested;
        }

        private async void Delete_Click(object sender, RoutedEventArgs e)
        {
            delete.IsChecked = false;
            if (isSelecting)
            {
                canvas.InkPresenter.StrokeContainer.DeleteSelected();
                Select_Clear();
            }
            else
            {
                ContentDialog clearDialog = new ContentDialog
                {
                    Title = "Clear the entire Canvas?",
                    Content = "Any nonsaved Changes won't be recoverable!",
                    PrimaryButtonText = "Delete",
                    CloseButtonText = "Cancel",
                    DefaultButton = ContentDialogButton.Primary
                };
                ContentDialogResult result = await clearDialog.ShowAsync();
                
                if (result == ContentDialogResult.Primary)
                {
                    canvas.InkPresenter.StrokeContainer.Clear();
                }

                UndoStrokes.Clear();
                alreadySaved = false;
            }
        }

        private async void Save_Click(object sender, RoutedEventArgs e)
        {
            save.IsChecked = false;

            StorageFile file;

            if (alreadySaved)
            {
                file = lastFile;
            }
            else
            {
                Windows.Storage.Pickers.FileSavePicker savePicker = new Windows.Storage.Pickers.FileSavePicker();
                savePicker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary;
                savePicker.FileTypeChoices.Add("GIF with embedded ISF", new List<string>() { ".gif" });
                savePicker.DefaultFileExtension = ".gif";
                savePicker.SuggestedFileName = "Sketch";

                file = await savePicker.PickSaveFileAsync();
            }
            if (file != null)
            {
                
                Windows.Storage.CachedFileManager.DeferUpdates(file);

                Windows.Storage.Streams.IRandomAccessStream stream = await file.OpenAsync(Windows.Storage.FileAccessMode.ReadWrite);
                using (Windows.Storage.Streams.IOutputStream outputStream = stream.GetOutputStreamAt(0))
                {
                    await canvas.InkPresenter.StrokeContainer.SaveAsync(outputStream);
                    await outputStream.FlushAsync();
                }
                stream.Dispose();

                Windows.Storage.Provider.FileUpdateStatus status = await Windows.Storage.CachedFileManager.CompleteUpdatesAsync(file);

                if (!alreadySaved)
                {
                    alreadySaved = true;
                    lastFile = file;
                }
            }
        }

        private async void Open_Click(object sender, RoutedEventArgs e)
        {
            open.IsChecked = false;


            Windows.Storage.Pickers.FileOpenPicker openPicker = new Windows.Storage.Pickers.FileOpenPicker();
            openPicker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.PicturesLibrary;
            openPicker.FileTypeFilter.Add(".gif");
            
            Windows.Storage.StorageFile file = await openPicker.PickSingleFileAsync();
            if (file != null)
            {
                Windows.Storage.Streams.IRandomAccessStream stream = await file.OpenAsync(Windows.Storage.FileAccessMode.Read);
                using (var inputStream = stream.GetInputStreamAt(0))
                {
                    await canvas.InkPresenter.StrokeContainer.LoadAsync(inputStream);
                }
                stream.Dispose();

                alreadySaved = true;
                lastFile = file;
            }
        }

        private void Stroke_Erased(object sender, InkStrokesErasedEventArgs e)
        {
            UndoStrokes.Push(e.Strokes[0]);
            Select_Clear();
        }

        private void Stroke_Started(InkStrokeInput sender, Windows.UI.Core.PointerEventArgs args)
        {
            Select_Clear();
        }

        private void Undo_Click(object sender, RoutedEventArgs e)
        {
            undo.IsChecked = false;

            IReadOnlyList<InkStroke> strokes = canvas.InkPresenter.StrokeContainer.GetStrokes();
            if (strokes.Count > 0)
            {
                strokes[strokes.Count - 1].Selected = true;
                UndoStrokes.Push(strokes[strokes.Count - 1]);
                canvas.InkPresenter.StrokeContainer.DeleteSelected();
            }
        }

        private void Redo_Click(object sender, RoutedEventArgs e)
        {
            redo.IsChecked = false;

            if (UndoStrokes.Count > 0)
            {
                var stroke = UndoStrokes.Pop();

                var strokeBuilder = new InkStrokeBuilder();
                strokeBuilder.SetDefaultDrawingAttributes(stroke.DrawingAttributes);
                System.Numerics.Matrix3x2 matr = stroke.PointTransform;
                IReadOnlyList<InkPoint> inkPoints = stroke.GetInkPoints();
                InkStroke stk = strokeBuilder.CreateStrokeFromInkPoints(inkPoints, matr);
                canvas.InkPresenter.StrokeContainer.AddStroke(stk);
            }
        }

        private void Touch_Click(object sender, RoutedEventArgs e)
        {
            if (touch.IsChecked == true)
            {
                canvas.InkPresenter.InputDeviceTypes |= Windows.UI.Core.CoreInputDeviceTypes.Touch;
            }
            else
            {
                canvas.InkPresenter.InputDeviceTypes &= ~Windows.UI.Core.CoreInputDeviceTypes.Touch;
            }
        }

        private void Select_Click(object sender, RoutedEventArgs e)
        {
            canvas.InkPresenter.InputProcessingConfiguration.RightDragAction = InkInputRightDragAction.LeaveUnprocessed;

            canvas.InkPresenter.UnprocessedInput.PointerPressed += Select_Pressed;
            canvas.InkPresenter.UnprocessedInput.PointerMoved += Select_Moved;
            canvas.InkPresenter.UnprocessedInput.PointerReleased += Select_Released;
        }

        private void Select_Clear()
        {
            var strokes = canvas.InkPresenter.StrokeContainer.GetStrokes();
            foreach (var stroke in strokes)
            {
                stroke.Selected = false;
            }

            Select_Clear_Bounds();
        }

        private void Select_Clear_Bounds()
        {
            if (selectionCanvas.Children.Any())
            {
                selectionCanvas.Children.Clear();
                boundingRect = Rect.Empty;
            }

            isSelecting = false;
        }

        private void Select_Pressed(InkUnprocessedInput sender, Windows.UI.Core.PointerEventArgs args)
        {
            if (isSelecting && boundingRect.Contains(args.CurrentPoint.RawPosition))
            {
                lastPoint = new Point(-10000, -10000);
                isMooving = true;
            }else
            {
                lasso = new Polyline()
                {
                    Stroke = new SolidColorBrush(Windows.UI.Colors.Black),
                    StrokeThickness = 2,
                    StrokeDashArray = new DoubleCollection() { 5, 5 },
                };

                lasso.Points.Add(args.CurrentPoint.RawPosition);

                selectionCanvas.Children.Add(lasso);
            }
        }

        private void Select_Moved(InkUnprocessedInput sender, Windows.UI.Core.PointerEventArgs args)
        {
            if (isMooving)
            {
                if(lastPoint.X == -10000 && lastPoint.Y == -10000)
                {
                    lastPoint = args.CurrentPoint.RawPosition;
                }
                else
                {
                    Point newPoint = args.CurrentPoint.RawPosition;
                    if (selectionCanvas.Children.Any())
                    {
                        selectionCanvas.Children.Clear();
                        boundingRect = Rect.Empty;
                    }
                    boundingRect = canvas.InkPresenter.StrokeContainer.MoveSelected(new Point(newPoint.X - lastPoint.X, newPoint.Y - lastPoint.Y));
                    Select_Draw_Bounds();

                    lastPoint = newPoint;
                }
                
            }
            else
            {
                lasso.Points.Add(args.CurrentPoint.RawPosition); ;
            }
        }

        private void Select_Released(InkUnprocessedInput sender, Windows.UI.Core.PointerEventArgs args)
        {
            if (isMooving) isMooving = false;
            else
            {
                lasso.Points.Add(args.CurrentPoint.RawPosition);

                boundingRect =
                    canvas.InkPresenter.StrokeContainer.SelectWithPolyLine(
                        lasso.Points);

                Select_Draw_Bounds();
            }
        }

        private void Select_Draw_Bounds()
        {
            isSelecting = true;
            selectionCanvas.Children.Clear();
            Rectangle rectangle = null;

            if (!((boundingRect.Width == 0) || (boundingRect.Height == 0) || boundingRect.IsEmpty))
            {
                rectangle = new Rectangle()
                {
                    Stroke = new SolidColorBrush(Windows.UI.Colors.Black),
                    StrokeThickness = 2,
                    StrokeDashArray = new DoubleCollection() { 5, 5 },
                    Width = boundingRect.Width,
                    Height = boundingRect.Height
                };

                Canvas.SetLeft(rectangle, boundingRect.X);
                Canvas.SetTop(rectangle, boundingRect.Y);

                selectionCanvas.Children.Add(rectangle);
            }
            
        }

        private void Cut_Click(object sender, RoutedEventArgs e)
        {
            cut.IsChecked = false;
            canvas.InkPresenter.StrokeContainer.CopySelectedToClipboard();
            canvas.InkPresenter.StrokeContainer.DeleteSelected();
            Select_Clear();
        }

        private void Copy_Click(object sender, RoutedEventArgs e)
        {
            copy.IsChecked = false;
            canvas.InkPresenter.StrokeContainer.CopySelectedToClipboard();
        }

        private void Paste_Click(object sender, RoutedEventArgs e)
        {
            paste.IsChecked = false;
            if (canvas.InkPresenter.StrokeContainer.CanPasteFromClipboard())
            {
                canvas.InkPresenter.StrokeContainer.PasteFromClipboard(
                    new Point(0, 0));
            }
        }

        private void Fullscreen_Click(object sender, RoutedEventArgs e)
        {
            var view = ApplicationView.GetForCurrentView();
            if (view.IsFullScreenMode)
            {
                view.ExitFullScreenMode();
            }
            else
            {
                view.TryEnterFullScreenMode();
            }
        }

        private async void Close_Click(object sender, RoutedEventArgs e)
        {
            close.IsEnabled = false;

            ContentDialog exitDialog = new ContentDialog
            {
                Title = "Exit?",
                Content = "Have you saved your latest Changes? They otherwise will be lost forever!",
                CloseButtonText = "Cancel",
                PrimaryButtonText = "Save",
                SecondaryButtonText = "Exit",
                DefaultButton = ContentDialogButton.Secondary
            };

            ContentDialogResult result = await exitDialog.ShowAsync();

            if (result == ContentDialogResult.Primary)
            {
                Save_Click(null, null);
            }
            else if (result == ContentDialogResult.Secondary)
            {
                Application.Current.Exit();
            }

        }

        private async void Close_Requested(object sender, SystemNavigationCloseRequestedPreviewEventArgs e)
        {
            Deferral deferral = e.GetDeferral();

            ContentDialog exitDialog = new ContentDialog
            {
                Title = "Exit?",
                Content = "Have you saved your latest Changes? They otherwise will be lost forever!",
                CloseButtonText = "Cancel",
                PrimaryButtonText = "Save",
                SecondaryButtonText = "Exit",
                DefaultButton = ContentDialogButton.Secondary
            };

            ContentDialogResult result = await exitDialog.ShowAsync();

            if (result == ContentDialogResult.Primary)
            {
                e.Handled = true;
                Save_Click(null, null);
            }
            else if (result != ContentDialogResult.Secondary)
            {
                e.Handled = true;
            }

            deferral.Complete();
        }
    }
}
