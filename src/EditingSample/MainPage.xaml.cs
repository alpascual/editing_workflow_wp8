using ESRI.ArcGIS.Client;
using ESRI.ArcGIS.Client.FeatureService;
using ESRI.ArcGIS.Client.Toolkit;
using ESRI.ArcGIS.Client.Toolkit.Primitives;
using Microsoft.Phone.Controls;
using Microsoft.Phone.Shell;
using Microsoft.Phone.Tasks;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace EditingSample
{
    public partial class MainPage : PhoneApplicationPage
    {
        private Editor editor; // Enables adding new features, editing existing geometry, saving and undoing edits on FeatureLayer.
        private bool showEditOptions; // Indicates whether feature needs to switch to edit mode.

        public MainPage()
        {
            InitializeComponent();

            ApplicationBar = (Microsoft.Phone.Shell.ApplicationBar)Resources["HomeAppBar"];

            showEditOptions = true;
            editor = LayoutRoot.Resources["MyEditor"] as Editor;
            
            // Event raised when an editor command becomes active
            editor.EditorActivated += (a, b) =>
                {
                    showEditOptions = false;
                    ChoicesPage.IsOpen = false;
                    ShowElement(MyMap);
                };

            // Event raised when an editor command has completed.
            editor.EditCompleted += (a, b) =>
                {
                    showEditOptions = true;
                };           
        }

        #region FeatureLayer events

        private void FeatureLayer_Initialized(object sender, EventArgs e)
        {
            if (editor == null || (sender as FeatureLayer).LayerInfo == null) return;
            var l = sender as FeatureLayer;

            #region Build FeatureDataForm
            // Use LayerInfo.Fields to build choice list for domain or feature type-driven fields.
            if (l.LayerInfo.Fields != null && l.LayerInfo.Fields.Count > 0)
            {
                // Field with CodedValueDomain
                var cvd = (from f in l.LayerInfo.Fields
                           where f.Name == "lifecyclestatus" && f.Domain is CodedValueDomain
                           select (f.Domain as CodedValueDomain).CodedValues).FirstOrDefault();
                StatusLB.ItemsSource = cvd;
                StatusLB.SetBinding(ListBox.DataContextProperty, new Binding("DataContext") { Source = FeatureDataForm });
                StatusLB.SetBinding(ListBox.SelectedItemProperty, new Binding(string.Format("Attributes[lifecyclestatus]"))
                {
                    Mode = BindingMode.TwoWay,
                    Converter = new CodeToValueConverter(),
                    ConverterParameter = cvd
                });

                // Field that defines FeatureType
                var ftypes = from t in l.LayerInfo.FeatureTypes select new KeyValuePair<object, string>(t.Key, t.Value.Name);
                FTypeLB.ItemsSource = ftypes;
                FTypeLB.SetBinding(ListBox.DataContextProperty, new Binding("DataContext") { Source = FeatureDataForm });
                FTypeLB.SetBinding(ListBox.SelectedItemProperty, new Binding(string.Format("Attributes[ftype]"))
                {
                    Mode = BindingMode.TwoWay,
                    Converter = new CodeToValueConverter(),
                    ConverterParameter = ftypes
                });
            }
            #endregion

            #region Build TemplatePicker
            // Use LayerInfo.FeatureTypes and FeatureTemplates with Editor.Add and SymbolDisplay to build TemplatePicker
            if (l.LayerInfo.FeatureTypes != null && l.LayerInfo.FeatureTypes.Count > 0)
            {
                foreach (var featureType in l.LayerInfo.FeatureTypes)
                {
                    if (featureType.Value.Templates != null && featureType.Value.Templates.Count > 0)
                    {
                        foreach (var featureTemplate in featureType.Value.Templates)
                        {
                            var sp = new StackPanel() { Orientation = System.Windows.Controls.Orientation.Horizontal };
                            sp.Children.Add(new Button()
                            {
                                Content = new SymbolDisplay()
                                {
                                    Height = 25,
                                    Width = 25,
                                    Symbol = featureTemplate.Value.GetSymbol(l.Renderer)
                                },
                                DataContext = editor,
                                CommandParameter = featureType.Value.Id,
                                Command = editor.Add
                            });
                            sp.Children.Add(new TextBlock() { Text = featureTemplate.Value.Name, VerticalAlignment = System.Windows.VerticalAlignment.Center });
                            TemplatePicker.Children.Add(sp);
                        }
                    }
                }
            }
            #endregion
        }

        private void FeatureLayer_MouseLeftButtonDown(object sender, GraphicMouseButtonEventArgs e)
        {
            if (showEditOptions)
            {
                // Ensures only one feature is selected and activated for edit.
                var l = sender as FeatureLayer;
                l.ClearSelection();
                e.Graphic.Select();
                // Displays edit options in an InfoWindow
                var clickPoint = MyMap.ScreenToMap(e.GetPosition(MyMap));
                var isNew = !e.Graphic.Attributes.ContainsKey("objectid");
                MyInfoWindow.ContentTemplate = isNew ? LayoutRoot.Resources["EditInfoWindowTemplateNewFeature"] as DataTemplate :
                    LayoutRoot.Resources["EditInfoWindowTemplate"] as DataTemplate;
                MyInfoWindow.Placement = InfoWindow.PlacementMode.Auto;
                MyInfoWindow.Anchor = clickPoint;
                MyInfoWindow.Content = e.Graphic;
                MyInfoWindow.IsOpen = true;
            }
        }

        private void FeatureLayer_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (editor == null || ApplicationBar == null || ApplicationBar.Buttons == null || ApplicationBar.Buttons.Count == 0)
                return;
            if (e.PropertyName == "HasEdits")
            {
                // Enables or disables ApplicationBar button based on Editor.Save state.
                foreach (var b in ApplicationBar.Buttons)
                    if ((b as ApplicationBarIconButton).Text == "Save" || (b as ApplicationBarIconButton).Text == "Cancel")
                        (b as ApplicationBarIconButton).IsEnabled = editor.Save.CanExecute(null);
            }
        }

        #endregion

        #region Edit Options from InfoWindow click event

        private void DeleteButton_Click(object sender, RoutedEventArgs e)
        {
            if (editor == null) return;
            var result = MessageBox.Show("Are you sure you want to delete this feature? This operation cannot be undone after edits are saved.", "Delete Feature", MessageBoxButton.OKCancel);
            if (result == MessageBoxResult.OK)
            {
                MyInfoWindow.IsOpen = false;
                // Deletes selected feature using Editor.DeleteSelected command.
                if (editor.DeleteSelected.CanExecute(null))
                    editor.DeleteSelected.Execute(null);
            }
        }
        
        private void EditGeometryButton_Click(object sender, RoutedEventArgs e)
        {
            if (editor == null) return;
            MyInfoWindow.IsOpen = false;
            var graphic = (sender as Button).DataContext as Graphic;
            // Enables editing of geometry of a specified graphic using Editor.EditVertices command.
            if (editor.EditVertices.CanExecute(graphic))
                editor.EditVertices.Execute(graphic);
        }
        
        private void AttachmentsButton_Click(object sender, RoutedEventArgs e)
        {
            var l = MyMap.Layers["ThreatAreas"] as FeatureLayer;
            var graphic = (sender as Button).DataContext as Graphic;
            // Populates a list of attachment if any is found on specified graphic.
            l.QueryAttachmentInfos(graphic, (attachments) =>
            {
                var attachmentsFound =  attachments.Count() ;
                AttachmentResult.Text = attachmentsFound == 0 ? "No attachments found." :
                    string.Format("{0} {1} found.", attachmentsFound, attachmentsFound == 1 ? "attachment" : "attachments");
                AttachmentsList.ItemsSource = attachments;               
                ShowElement(AttachmentsGrid);
            },
            (exception) =>
            {
                MessageBox.Show(string.Format("Query attachments failed with error'{0}'.", exception.Message));
                ShowElement(MyMap);
            });
        }

        private void EditAttributesButton_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            var graphic = (sender as Button).DataContext as Graphic;
            // Enables feature attribute editing by setting the DataContext of a FeatureDataForm
            // so that TextBox or ListBox can resolve their two-way binding.
            FeatureDataForm.DataContext = graphic;
            ShowElement(FeatureDataFormGrid);
        }
        
        #endregion

        #region Application Bar Icon click event

        private void AddIconButton_Click(object sender, EventArgs e)
        {
            ShowElement(TemplatePickerGrid);
        }

        private void SaveIconButton_Click(object sender, EventArgs e)
        {
            if (editor == null) return;
            if (editor.Save.CanExecute(null))
                editor.Save.Execute(null);
        }

        private void CancelIconButton_Click(object sender, EventArgs e)
        {
            if (editor == null) return;
            if (editor.UndoEdits.CanExecute(null))
                editor.UndoEdits.Execute(null);
        }

        private void AddAttachmentIconButton_Click(object sender, EventArgs e)
        {
            PhotoChooserTask pct = new PhotoChooserTask() { ShowCamera = true };
            pct.Completed += (a, b) =>
            {
                if (b.ChosenPhoto != null)
                {
                    var l = MyMap.Layers["ThreatAreas"] as FeatureLayer;
                    var selectedGraphic = l.SelectedGraphics.FirstOrDefault();
                    var fileName = System.IO.Path.GetFileName(b.OriginalFileName);
                    l.AddAttachment(selectedGraphic, b.ChosenPhoto, fileName,
                        (attachmentResult) =>
                        {
                            if (attachmentResult.Success)
                                MessageBox.Show(string.Format("'{0}' was uploaded to '{1}'", fileName, attachmentResult.ObjectID));
                            else
                                MessageBox.Show(string.Format("'{0}' failed to upload", fileName));
                            ShowElement(MyMap);
                        },
                        (exception) =>
                        {
                            MessageBox.Show(string.Format("'{0}' failed to upload with error '{1}'", fileName, exception.Message));
                            ShowElement(MyMap);
                        });
                }
                else if (b.Error != null)
                {
                    MessageBox.Show(string.Format("No photo was selected with error '{0}'", b.Error.Message));
                    ShowElement(MyMap);
                }
            };
            pct.Show();
        }

        private void ShowMapIconButton_Click(object sender, EventArgs e)
        {
            ShowElement(MyMap);
        }

        #endregion

        #region Helper methods

        private void ShowElement(UIElement element)
        {
            if (element != MyMap)
            {
                MyInfoWindow.IsOpen = false;
                if (element == TemplatePicker)
                    ApplicationBar = null;
                else if (element == FeatureDataFormGrid)
                    ApplicationBar = (Microsoft.Phone.Shell.ApplicationBar)Resources["DataFormAppBar"];
                else if (element == AttachmentsGrid)
                    ApplicationBar = (Microsoft.Phone.Shell.ApplicationBar)Resources["AttachmentAppBar"];
            }
            else
                ApplicationBar = (Microsoft.Phone.Shell.ApplicationBar)Resources["HomeAppBar"];              
            TemplatePickerGrid.Visibility = TemplatePickerGrid == element ? Visibility.Visible : Visibility.Collapsed;
            FeatureDataFormGrid.Visibility = FeatureDataFormGrid == element ? Visibility.Visible : Visibility.Collapsed;
            AttachmentsGrid.Visibility = AttachmentsGrid == element ? Visibility.Visible : Visibility.Collapsed;
        }

        private void DeleteAttachmentButton_Click(object sender, RoutedEventArgs e)
        {
            var info = (sender as Button).DataContext as AttachmentInfo;
            if (info == null) return;
            var result = MessageBox.Show("Are you sure you want to delete this attachment? This operation cannot be undone.", "Delete Attachment", MessageBoxButton.OKCancel);
            if (result == MessageBoxResult.OK)
            {
                // Deletes selected the attachment upon confirmation
                // using Delete command from AttachmentInfo.
                if (info.Delete.CanExecute(null))
                    info.Delete.Execute(null);
                ShowElement(MyMap);
            }
        }

        private void MyInfoWindow_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            MyInfoWindow.IsOpen = false;         
        }

        private void EnableChoiceListButton_Click(object sender, RoutedEventArgs e)
        {
            var tag = (sender as Button).Tag.ToString();
            StatusLB.Visibility = (tag == "Status") ? Visibility.Visible : Visibility.Collapsed;
            FTypeLB.Visibility = (tag == "FType") ? Visibility.Visible : Visibility.Collapsed;
            ChoicesPage.IsOpen = true;
        }

        private void ChoiceList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ChoicesPage.IsOpen = false;
        }

        private void CloseButtonClick(object sender, RoutedEventArgs e)
        {
            ChoicesPage.IsOpen = false;
        }

        #endregion
    }

    /// <summary>
    /// Converts code to value using CodedValueDomain information from Field.Domain 
    /// or FeatureType lookup built from LayerInfo.FeatureTypes.
    /// </summary>
    public class CodeToValueConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            if (value == null) return value;
            if (parameter is IEnumerable<KeyValuePair<object, string>>)
            {
                var lookup = parameter as IEnumerable<KeyValuePair<object, string>>;
                if (value is int)
                {
                    value = (from l in lookup
                             where l.Key is int && (int)l.Key == (int)value
                             select l).FirstOrDefault();
                }
            }
            return value;
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            var kvp = (KeyValuePair<object, string>)value;
            value = kvp.Key;
            return value;
        }
    }
}