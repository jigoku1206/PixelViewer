using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Visuals.Media.Imaging;
using Avalonia.VisualTree;
using Carina.PixelViewer.Media.Profiles;
using Carina.PixelViewer.ViewModels;
using CarinaStudio;
using CarinaStudio.AppSuite;
using CarinaStudio.AppSuite.Controls;
using CarinaStudio.Collections;
using CarinaStudio.Configuration;
using CarinaStudio.Controls;
using CarinaStudio.Data.Converters;
using CarinaStudio.Input;
using CarinaStudio.Threading;
using CarinaStudio.Windows.Input;
using Microsoft.Extensions.Logging;
using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;

namespace Carina.PixelViewer.Controls
{
	/// <summary>
	/// <see cref="Control"/>(View) of <see cref="Session"/>.
	/// </summary>
	class SessionControl : UserControl<IAppSuiteApplication>
	{
		/// <summary>
		/// <see cref="IValueConverter"/> which maps boolean to <see cref="Stretch.Uniform"/>(True) and <see cref="Stretch.None"/>(False).
		/// </summary>
		public static readonly IValueConverter BooleanToMediaStretchConverter = new BooleanToValueConverter<Stretch>(Stretch.Uniform, Stretch.None);
		/// <summary>
		/// <see cref="IValueConverter"/> which maps boolean to <see cref="ScrollBarVisibility.Auto"/>(True) and <see cref="ScrollBarVisibility.Disabled"/>(False).
		/// </summary>
		public static readonly IValueConverter BooleanToScrollBarVisibilityConverter = new BooleanToValueConverter<ScrollBarVisibility>(ScrollBarVisibility.Auto, ScrollBarVisibility.Disabled);
		/// <summary>
		/// Property of <see cref="EffectiveRenderedImageScale"/>.
		/// </summary>
		public static readonly AvaloniaProperty<double> EffectiveRenderedImageScaleProperty = AvaloniaProperty.Register<SessionControl, double>(nameof(EffectiveRenderedImageScale), 1.0);


		// Constants.
		const int HidePanelsByImageViewerSizeDelay = 500;
		const int StopUsingSmallRenderedImageDelay = 1000;


		// Static fields.
		static readonly AvaloniaProperty<IImage?> EffectiveRenderedImageProperty = AvaloniaProperty.Register<SessionControl, IImage?>(nameof(EffectiveRenderedImage));
		static readonly AvaloniaProperty<BitmapInterpolationMode> EffectiveRenderedImageInterpolationModeProperty = AvaloniaProperty.Register<SessionControl, BitmapInterpolationMode>(nameof(EffectiveRenderedImageInterpolationMode), BitmapInterpolationMode.Default);
		static readonly AvaloniaProperty<bool> IsImageViewerScrollableProperty = AvaloniaProperty.Register<SessionControl, bool>(nameof(IsImageViewerScrollable));
		static readonly AvaloniaProperty<bool> ShowProcessInfoProperty = AvaloniaProperty.Register<SessionControl, bool>(nameof(ShowProcessInfo));
		static readonly AvaloniaProperty<StatusBarState> StatusBarStateProperty = AvaloniaProperty.Register<SessionControl, StatusBarState>(nameof(StatusBarState), StatusBarState.None);


		// Fields.
		Avalonia.Controls.Window? attachedWindow;
		readonly ToggleButton brightnessAndContrastAdjustmentButton;
		readonly Popup brightnessAndContrastAdjustmentPopup;
		readonly MutableObservableValue<bool> canOpenSourceFile = new MutableObservableValue<bool>();
		readonly MutableObservableValue<bool> canResetBrightnessAndContrastAdjustment = new MutableObservableValue<bool>();
		readonly MutableObservableValue<bool> canSaveAsNewProfile = new MutableObservableValue<bool>();
		readonly MutableObservableValue<bool> canSaveImage = new MutableObservableValue<bool>();
		readonly MutableObservableValue<bool> canShowEvaluateImageDimensionsMenu = new MutableObservableValue<bool>();
		readonly ToggleButton colorAdjustmentButton;
		readonly Popup colorAdjustmentPopup;
		readonly ToggleButton evaluateImageDimensionsButton;
		readonly ContextMenu evaluateImageDimensionsMenu;
		readonly ToggleButton fileActionsButton;
		readonly ContextMenu fileActionsMenu;
		readonly ScheduledAction hidePanelsByImageViewerSizeAction;
		readonly ToggleButton histogramsButton;
		Vector? imagePointerPressedContentPosition;
		readonly ComboBox imageRendererComboBox;
		readonly ScrollViewer imageScrollViewer;
		readonly Control imageViewerGrid;
		bool isFirstImageViewerBoundsChanged = true;
		bool keepHistogramsVisible;
		bool keepRenderingParamsPanelVisible;
		readonly double minImageViewerSizeToHidePanels;
		readonly ToggleButton otherActionsButton;
		readonly ContextMenu otherActionsMenu;
		readonly ColumnDefinition renderingParamsPanelColumn;
		readonly ScheduledAction stopUsingSmallRenderedImageAction;
		Vector? targetImageViewportCenter;
		readonly ScheduledAction updateEffectiveRenderedImageAction;
		readonly ScheduledAction updateEffectiveRenderedImageIntModeAction;
		readonly ScheduledAction updateIsImageViewerScrollableAction;
		readonly ScheduledAction updateStatusBarStateAction;
		bool useSmallRenderedImage;


		/// <summary>
		/// Initialize new <see cref="SessionControl"/> instance.
		/// </summary>
		public SessionControl()
		{
			// create commands
			this.OpenSourceFileCommand = new Command(this.OpenSourceFile, this.canOpenSourceFile);
			this.ResetBrightnessAndContrastAdjustmentCommand = new Command(this.ResetBrightnessAndContrastAdjustment, this.canResetBrightnessAndContrastAdjustment);
			this.SaveAsNewProfileCommand = new Command(() => this.SaveAsNewProfile(), this.canSaveAsNewProfile);
			this.SaveImageCommand = new Command(() => this.SaveImage(), this.canSaveImage);
			this.ShowEvaluateImageDimensionsMenuCommand = new Command(() =>
			{
				if (this.evaluateImageDimensionsMenu == null)
					return;
				if (this.evaluateImageDimensionsMenu.PlacementTarget == null)
					this.evaluateImageDimensionsMenu.PlacementTarget = this.evaluateImageDimensionsButton;
				this.evaluateImageDimensionsMenu.Open(this.evaluateImageDimensionsButton);
			}, this.canShowEvaluateImageDimensionsMenu);
			this.canOpenSourceFile.Update(true);

			// load layout
			AvaloniaXamlLoader.Load(this);

			// [Workaround] setup initial command state after loading XAML
			this.canOpenSourceFile.Update(false);

			// setup controls
			this.brightnessAndContrastAdjustmentButton = this.FindControl<ToggleButton>(nameof(brightnessAndContrastAdjustmentButton)).AsNonNull();
			this.brightnessAndContrastAdjustmentPopup = this.FindControl<Popup>(nameof(brightnessAndContrastAdjustmentPopup)).AsNonNull().Also(it =>
			{
				it.PlacementTarget = this.brightnessAndContrastAdjustmentButton;
				it.Closed += (_, e) => this.SynchronizationContext.Post(() => this.brightnessAndContrastAdjustmentButton.IsChecked = false);
				it.Opened += (_, e) => this.SynchronizationContext.Post(() => this.brightnessAndContrastAdjustmentButton.IsChecked = true);
			});
			this.colorAdjustmentButton = this.FindControl<ToggleButton>(nameof(colorAdjustmentButton)).AsNonNull();
			this.colorAdjustmentPopup = this.FindControl<Popup>(nameof(colorAdjustmentPopup)).AsNonNull().Also(it =>
			{
				it.PlacementTarget = this.colorAdjustmentButton;
				it.Closed += (_, e) => this.SynchronizationContext.Post(() => this.colorAdjustmentButton.IsChecked = false);
				it.Opened += (_, e) => this.SynchronizationContext.Post(() => this.colorAdjustmentButton.IsChecked = true);
			});
			this.evaluateImageDimensionsButton = this.FindControl<ToggleButton>(nameof(this.evaluateImageDimensionsButton)).AsNonNull();
			this.evaluateImageDimensionsMenu = ((ContextMenu)this.Resources[nameof(evaluateImageDimensionsMenu)].AsNonNull()).Also(it =>
			{
				it.MenuClosed += (_, e) => this.SynchronizationContext.Post(() => this.evaluateImageDimensionsButton.IsChecked = false);
				it.MenuOpened += (_, e) => this.SynchronizationContext.Post(() => this.evaluateImageDimensionsButton.IsChecked = true);
			});
			this.fileActionsButton = this.FindControl<ToggleButton>(nameof(this.fileActionsButton)).AsNonNull();
			this.fileActionsMenu = ((ContextMenu)this.Resources[nameof(fileActionsMenu)].AsNonNull()).Also(it =>
			{
				it.MenuClosed += (_, e) => this.SynchronizationContext.Post(() => this.fileActionsButton.IsChecked = false);
				it.MenuOpened += (_, e) => this.SynchronizationContext.Post(() => this.fileActionsButton.IsChecked = true);
			});
			this.histogramsButton = this.FindControl<ToggleButton>(nameof(histogramsButton)).AsNonNull();
			this.imageRendererComboBox = this.FindControl<ComboBox>(nameof(imageRendererComboBox)).AsNonNull();
			this.imageScrollViewer = this.FindControl<ScrollViewer>(nameof(this.imageScrollViewer)).AsNonNull();
			this.imageViewerGrid = this.FindControl<Control>(nameof(imageViewerGrid)).AsNonNull().Also(it =>
			{
				it.GetObservable(BoundsProperty).Subscribe(new Observer<Rect>((_) =>
				{
					if (this.isFirstImageViewerBoundsChanged)
					{
						this.isFirstImageViewerBoundsChanged = false;
						this.hidePanelsByImageViewerSizeAction?.Reschedule();
					}
					else
						this.hidePanelsByImageViewerSizeAction?.Schedule(HidePanelsByImageViewerSizeDelay);
				}));
			});
			this.otherActionsButton = this.FindControl<ToggleButton>(nameof(otherActionsButton)).AsNonNull();
			this.otherActionsMenu = ((ContextMenu)this.Resources[nameof(otherActionsMenu)].AsNonNull()).Also(it =>
			{
				it.MenuClosed += (_, e) => this.SynchronizationContext.Post(() => this.otherActionsButton.IsChecked = false);
				it.MenuOpened += (_, e) => this.SynchronizationContext.Post(() => this.otherActionsButton.IsChecked = true);
			});
			this.renderingParamsPanelColumn = this.FindControl<Grid>("workingAreaGrid").AsNonNull().ColumnDefinitions.Last().Also(column =>
			{
				column.GetObservable(ColumnDefinition.WidthProperty).Subscribe(new Observer<GridLength>((_) =>
				{
					(this.DataContext as Session)?.Let(it => it.RenderingParametersPanelSize = column.Width.Value);
				}));
			});
#if DEBUG
			this.FindControl<Button>("testButton").AsNonNull().IsVisible = true;
#endif

			// load resources
			if (this.Application.TryGetResource<double>("Double/SessionControl.ImageViewer.MinSizeToHidePanels", out var doubleRes))
				this.minImageViewerSizeToHidePanels = doubleRes.GetValueOrDefault();

			// create scheduled actions
			this.hidePanelsByImageViewerSizeAction = new ScheduledAction(() =>
			{
				if (this.imageViewerGrid.Bounds.Width > this.minImageViewerSizeToHidePanels)
				{
					this.keepHistogramsVisible = false;
					this.keepRenderingParamsPanelVisible = false;
					return;
				}
				if (this.DataContext is not Session session)
					return;
				if (session.IsRenderingParametersPanelVisible && !this.keepRenderingParamsPanelVisible)
				{
					session.IsRenderingParametersPanelVisible = false;
					return;
				}
				else
					this.keepRenderingParamsPanelVisible = false;
				if (!this.keepHistogramsVisible)
					session.IsHistogramsVisible = false;
				else
					this.keepHistogramsVisible = false;
			});
			this.stopUsingSmallRenderedImageAction = new ScheduledAction(() =>
			{
				if (this.useSmallRenderedImage)
				{
					this.useSmallRenderedImage = false;
					this.updateEffectiveRenderedImageAction?.Schedule();
					this.updateEffectiveRenderedImageIntModeAction?.Schedule();
				}
			});
			this.updateEffectiveRenderedImageAction = new ScheduledAction(() =>
			{
				if (this.DataContext is not Session session)
					this.SetValue<IImage?>(EffectiveRenderedImageProperty, null);
				else if (this.useSmallRenderedImage && session.HasQuarterSizeRenderedImage)
					this.SetValue<IImage?>(EffectiveRenderedImageProperty, session.QuarterSizeRenderedImage);
				else
					this.SetValue<IImage?>(EffectiveRenderedImageProperty, session.RenderedImage);
			});
			this.updateEffectiveRenderedImageIntModeAction = new ScheduledAction(() =>
			{
				if (this.DataContext is not Session session)
					return;
				if (useSmallRenderedImage)
					this.SetValue<BitmapInterpolationMode>(EffectiveRenderedImageInterpolationModeProperty, BitmapInterpolationMode.LowQuality);
				else if (session.FitRenderedImageToViewport || this.EffectiveRenderedImageScale < 1)
					this.SetValue<BitmapInterpolationMode>(EffectiveRenderedImageInterpolationModeProperty, BitmapInterpolationMode.HighQuality);
				else
					this.SetValue<BitmapInterpolationMode>(EffectiveRenderedImageInterpolationModeProperty, BitmapInterpolationMode.LowQuality);
			});
			this.updateIsImageViewerScrollableAction = new ScheduledAction(() =>
			{
				var contentSize = this.imageScrollViewer.Extent;
				var viewport = this.imageScrollViewer.Viewport;
				this.SetValue<bool>(IsImageViewerScrollableProperty, contentSize.Width > viewport.Width || contentSize.Height > viewport.Height);
			});
			this.updateStatusBarStateAction = new ScheduledAction(() =>
			{
				this.SetValue<StatusBarState>(StatusBarStateProperty, Global.Run(() =>
				{
					if (this.DataContext is not Session session)
						return StatusBarState.Inactive;
					if (session.HasRenderingError || session.InsufficientMemoryForRenderedImage)
						return StatusBarState.Error;
					if (session.IsSourceFileOpened)
						return StatusBarState.Active;
					return StatusBarState.Inactive;
				}));
			});
		}


		// Check for application update.
		void CheckForAppUpdate()
		{
			this.FindLogicalAncestorOfType<Avalonia.Controls.Window>()?.Let(async (window) =>
			{
				using var updater = new CarinaStudio.AppSuite.ViewModels.ApplicationUpdater();
				var result = await new CarinaStudio.AppSuite.Controls.ApplicationUpdateDialog(updater)
				{
					CheckForUpdateWhenShowing = true
				}.ShowDialog(window);
				if (result == ApplicationUpdateDialogResult.ShutdownNeeded)
				{
					Logger.LogWarning("Shut down to continue updating");
					this.Application.Shutdown();
				}
			});
		}


		// Copy file name.
		void CopyFileName()
		{
			if (this.DataContext is not Session session || !session.IsSourceFileOpened)
				return;
			session.SourceFileName?.Let(it =>
			{
				_ = ((App)this.Application)?.Clipboard?.SetTextAsync(Path.GetFileName(it));
			});
		}


		// Copy file path.
		void CopyFilePath()
		{
			if (this.DataContext is not Session session || !session.IsSourceFileOpened)
				return;
			session.SourceFileName?.Let(it =>
			{
				_ = ((App)this.Application)?.Clipboard?.SetTextAsync(it);
			});
		}


		/// <summary>
		/// Drop data to this control.
		/// </summary>
		/// <param name="data">Dropped data.</param>
		/// <param name="keyModifiers">Key modifiers.</param>
		/// <returns>True if data has been accepted.</returns>
		public async Task<bool> DropDataAsync(IDataObject data, KeyModifiers keyModifiers)
		{
			// get file names
			var fileNames = data.GetFileNames()?.ToArray();
			if (fileNames == null || fileNames.IsEmpty())
				return false;

			// get window
			var window = this.FindLogicalAncestorOfType<Avalonia.Controls.Window>();
			if (window == null)
				return false;

			// check file count
			if (fileNames.Length > 8)
			{
				await new MessageDialog()
				{
					Icon = MessageDialogIcon.Warning,
					Message = this.Application.GetString("SessionControl.MaxDragDropFileCountReached"),
				}.ShowDialog(window);
				return false;
			}

			// open files
			if (fileNames.Length > 1)
			{
				// select profile
				var profile = await new ImageRenderingProfileSelectionDialog()
				{
					Message = this.Application.GetString("SessionControl.SelectProfileToOpenFiles"),
				}.ShowDialog<ImageRenderingProfile?>(window);
				if (profile == null)
					return false;

				// get workspace
				if (this.DataContext is not Session session || session.Owner is not Workspace workspace)
					return false;

				// create sessions
				foreach (var fileName in fileNames)
				{
					if (session.SourceFileName != null)
						workspace.CreateSession(fileName, profile);
					else
					{
						session.OpenSourceFileCommand.TryExecute(fileName);
						session.Profile = profile;
					}
				}
			}
			else if (this.Settings.GetValueOrDefault(SettingKeys.CreateNewSessionForDragDropFile)
					&& this.DataContext is Session session
					&& session.SourceFileName != null
					&& session.Owner is Workspace workspace)
			{
				workspace.CreateSession(fileNames[0]);
			}
			else
				this.OpenSourceFile(fileNames[0]);
			return true;
		}


		// Effective rendered image to display.
		IImage? EffectiveRenderedImage { get => this.GetValue<IImage?>(EffectiveRenderedImageProperty); }


		// Interpolation mode for rendered image.
		BitmapInterpolationMode EffectiveRenderedImageInterpolationMode { get => this.GetValue<BitmapInterpolationMode>(EffectiveRenderedImageInterpolationModeProperty); }


		/// <summary>
		/// Get effective scaling ratio of rendered image.
		/// </summary>
		public double EffectiveRenderedImageScale { get => this.GetValue<double>(EffectiveRenderedImageScaleProperty); }


		// Check whether image viewer is scrollable in current state or not.
		bool IsImageViewerScrollable { get => this.GetValue<bool>(IsImageViewerScrollableProperty); }


		// OS type.
		bool IsNotMacOS { get; } = !CarinaStudio.Platform.IsMacOS;


		// Move to specific frame.
		async void MoveToSpecificFrame()
		{
			// check state
			if (this.DataContext is not Session session)
				return;
			if (!session.HasMultipleFrames)
				return;

			// find window
			var window = this.FindLogicalAncestorOfType<Avalonia.Controls.Window>();
			if (window == null)
				return;

			// select frame number
			var selectFrameNumber = await new FrameNumberSelectionDialog()
			{
				FrameCount = session.FrameCount,
				InitialFrameNumber = session.FrameNumber,
			}.ShowDialog<int?>(window);
			if (selectFrameNumber == null)
				return;

			// move to frame
			if (this.DataContext == session)
				session.FrameNumber = selectFrameNumber.Value;
		}


		// Application string resources updated.
		void OnApplicationStringsUpdated(object? sender, EventArgs e)
		{
			var imageRendererTemplate = this.imageRendererComboBox.ItemTemplate;
			this.imageRendererComboBox.ItemTemplate = null;
			this.imageRendererComboBox.ItemTemplate = imageRendererTemplate;
		}


		// Called when attached to logical tree.
		protected override void OnAttachedToLogicalTree(LogicalTreeAttachmentEventArgs e)
		{
			// call base
			base.OnAttachedToLogicalTree(e);

			// enable drag-drop
			this.AddHandler(DragDrop.DragOverEvent, this.OnDragOver);
			this.AddHandler(DragDrop.DropEvent, this.OnDrop);

			// add event handlers
			this.Application.StringsUpdated += this.OnApplicationStringsUpdated;
			this.AddHandler(PointerWheelChangedEvent, this.OnPointerWheelChanged, Avalonia.Interactivity.RoutingStrategies.Tunnel);

			// attach to settings
			var settings = this.Settings;
			settings.SettingChanged += this.OnSettingChanged;
			this.SetValue<bool>(ShowProcessInfoProperty, settings.GetValueOrDefault(SettingKeys.ShowProcessInfo));

			// attach to window
			this.attachedWindow = this.FindLogicalAncestorOfType<Avalonia.Controls.Window>()?.Also(it =>
			{
				it.PropertyChanged += this.OnWindowPropertyChanged;
			});
		}


		// Called when attached to visual tree.
        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
			// call base
            base.OnAttachedToVisualTree(e);

			// update state
			this.isFirstImageViewerBoundsChanged = true;

			// [Workaround] Force refreshing status bar state to make background applied as expected
			this.SetValue<StatusBarState>(StatusBarStateProperty, StatusBarState.None);
			this.updateStatusBarStateAction.Reschedule();
		}


        // Called when detached from logical tree.
        protected override void OnDetachedFromLogicalTree(LogicalTreeAttachmentEventArgs e)
		{
			// disable drag-drop
			this.RemoveHandler(DragDrop.DragOverEvent, this.OnDragOver);
			this.RemoveHandler(DragDrop.DropEvent, this.OnDrop);

			// remove event handlers
			this.Application.StringsUpdated -= this.OnApplicationStringsUpdated;
			this.RemoveHandler(PointerWheelChangedEvent, this.OnPointerWheelChanged);

			// detach from settings
			this.Settings.SettingChanged -= this.OnSettingChanged;

			// detach from window
			this.attachedWindow = this.attachedWindow?.Let(it =>
			{
				it.PropertyChanged -= this.OnWindowPropertyChanged;
				return (Avalonia.Controls.Window?)null;
			});

			// call base
			base.OnDetachedFromLogicalTree(e);
		}


		// Called when drag over.
		void OnDragOver(object? sender, DragEventArgs e)
		{
			if (e.Data.HasFileNames())
			{
				e.DragEffects = DragDropEffects.Copy;
				e.Handled = true;
			}
			else
				e.DragEffects = DragDropEffects.None;
		}


		// Called when drop.
		void OnDrop(object? sender, DragEventArgs e)
		{
			_ = this.DropDataAsync(e.Data, e.KeyModifiers);
			e.Handled = true;
		}


        // Called when key down.
        protected override void OnKeyDown(Avalonia.Input.KeyEventArgs e)
        {
			// call base
			base.OnKeyDown(e);
			if (e.Handled)
				return;

			// check focus
			var focusedElement = Avalonia.Input.FocusManager.Instance?.Current;
			if (focusedElement != null)
			{
				if (focusedElement is TextBox || focusedElement is NumericUpDown)
					return;
				if (focusedElement.FindAncestorOfType<SessionControl>(true) != this)
					return;
			}

			// get session
			if (this.DataContext is not Session session)
				return;

			// handle key event
			if ((e.KeyModifiers & KeyModifiers.Control) != 0)
			{
				switch (e.Key)
				{
					case Avalonia.Input.Key.D0:
						{
							session.FitRenderedImageToViewport = true;
							break;
						}
					case Avalonia.Input.Key.D1:
						{
							if (session.FitRenderedImageToViewport)
							{
								session.RenderedImageScale = 1.0;
								session.FitRenderedImageToViewport = false;
							}
							else
								session.ZoomToCommand.TryExecute(1.0);
							break;
						}
					case Avalonia.Input.Key.O:
						{
							this.OpenSourceFile();
							break;
						}
					case Avalonia.Input.Key.OemPlus:
						{
							session.ZoomInCommand.Execute(null);
							break;
						}
					case Avalonia.Input.Key.OemMinus:
						{
							session.ZoomOutCommand.Execute(null);
							break;
						}
					case Avalonia.Input.Key.S:
						{
							this.SaveImage();
							break;
						}
					default:
						return;
				}
				e.Handled = true;
			}
		}


        // Called when key up.
        protected override void OnKeyUp(Avalonia.Input.KeyEventArgs e)
		{
			// call base
			base.OnKeyUp(e);
			if (e.Handled)
				return;

			// check focus
			var focusedElement = Avalonia.Input.FocusManager.Instance?.Current;
			if (focusedElement != null)
			{
				if (focusedElement is TextBox || focusedElement is NumericUpDown)
					return;
				if (focusedElement.FindAncestorOfType<SessionControl>(true) != this)
					return;
			}

			// get session
			if (this.DataContext is not Session session)
				return;

			// handle key event
			if (e.KeyModifiers == 0)
			{
				switch (e.Key)
				{
					case Avalonia.Input.Key.End:
						session.MoveToLastFrameCommand.TryExecute();
						break;
					case Avalonia.Input.Key.Home:
						session.MoveToFirstFrameCommand.TryExecute();
						break;
					case Avalonia.Input.Key.PageDown:
						session.MoveToNextFrameCommand.TryExecute();
						break;
					case Avalonia.Input.Key.PageUp:
						session.MoveToPreviousFrameCommand.TryExecute();
						break;
					default:
						return;
				}
				e.Handled = true;
			}
		}


		// Called when double tap on image.
		void OnImageDoubleTapped(object? sender, RoutedEventArgs e)
		{
			if (this.DataContext is not Session session)
				return;
			if (session.FitRenderedImageToViewport)
				session.FitRenderedImageToViewport = false;
			else if (session.ZoomInCommand.CanExecute(null))
				session.ZoomInCommand.TryExecute();
			else if (session.ZoomToCommand.CanExecute(1.0))
				session.ZoomToCommand.TryExecute(1.0);
		}


		// Called when pointer leave from image.
		void OnImagePointerLeave(object sender, PointerEventArgs e)
		{
			(this.DataContext as Session)?.SelectRenderedImagePixel(-1, -1);
		}


		// Called when pointer moved on image.
		void OnImagePointerMoved(object sender, PointerEventArgs e)
		{
			// move image
			this.imagePointerPressedContentPosition?.Let(it =>
			{
				var point = e.GetCurrentPoint(this.imageScrollViewer);
				if (point.Properties.IsLeftButtonPressed)
				{
					var bounds = this.imageScrollViewer.Bounds;
					if (!bounds.IsEmpty)
						this.ScrollImageScrollViewer(it, new Vector(point.Position.X / bounds.Width, point.Position.Y / bounds.Height));
				}
				else
				{
					this.imagePointerPressedContentPosition = null;
					this.stopUsingSmallRenderedImageAction.Schedule();
				}
			});

			// select pixel on image
			var position = e.GetPosition(sender as IVisual);
			(this.DataContext as Session)?.SelectRenderedImagePixel((int)(position.X + 0.5), (int)(position.Y + 0.5));
		}


		// Called when pressing on image viewer.
		void OnImagePointerPressed(object? sender, PointerPressedEventArgs e)
		{
			if (e.Pointer.Type == PointerType.Mouse && this.IsImageViewerScrollable)
			{
				var pointer = e.GetCurrentPoint(this.imageScrollViewer);
				if (pointer.Properties.IsLeftButtonPressed)
				{
					var contentSize = this.imageScrollViewer.Extent;
					var offset = this.imageScrollViewer.Offset;
					if (contentSize.Width > 0 && contentSize.Height > 0)
					{
						this.imagePointerPressedContentPosition = new Vector(
							(pointer.Position.X + offset.X) / contentSize.Width, 
							(pointer.Position.Y + offset.Y) / contentSize.Height);
						this.StartUsingSmallRenderedImage();
					}
				}
			}
		}


		// Called when releasing pointer from image viewer.
		void OnImagePointerReleased(object? sender, PointerReleasedEventArgs e)
		{
			this.imagePointerPressedContentPosition = null;
			this.stopUsingSmallRenderedImageAction.Schedule();
		}


		// Called when pressing on image scroll viewer.
		void OnImageScrollViewerPointerPressed(object? sender, PointerPressedEventArgs e)
		{
			this.imageScrollViewer.Focus();
		}


		// Called when property of image scroll viewer changed.
		void OnImageScrollViewerPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
		{
			if (e.Property == ScrollViewer.ExtentProperty)
			{
				this.updateIsImageViewerScrollableAction.Schedule();
				if (this.targetImageViewportCenter.HasValue)
				{
					this.SynchronizationContext.Post(() =>
					{
						var center = this.targetImageViewportCenter.Value;
						this.targetImageViewportCenter = null;
						this.ScrollImageScrollViewer(center, new Vector(0.5, 0.5));
					});
				}
			}
			else if (e.Property == ScrollViewer.ViewportProperty)
				this.updateIsImageViewerScrollableAction.Schedule();
		}


		// Called when complete dragging splitter of options panel.
		void OnOptionsPanelSplitterDragCompleted(object? sender, VectorEventArgs e) =>
			this.stopUsingSmallRenderedImageAction.Schedule();


		// Called when start dragging splitter of options panel.
		void OnOptionsPanelSplitterDragStarted(object? sender, VectorEventArgs e) =>
			this.StartUsingSmallRenderedImage();


		// Called when changing mouse wheel.
		void OnPointerWheelChanged(object? sender, PointerWheelEventArgs e)
		{
			if (!this.imageScrollViewer.IsPointerOver || (e.KeyModifiers & KeyModifiers.Control) == 0)
				return;
			if (this.DataContext is not Session session || !session.IsSourceFileOpened || session.FitRenderedImageToViewport)
				return;
			var zoomed = false;
			if (e.Delta.Y > 0)
			{
				for (var i = (int)(e.Delta.Y + 0.5); i > 0; --i)
				{
					if (session.ZoomInCommand.TryExecute())
						zoomed = true;
				}
			}
			else if (e.Delta.Y < 0)
			{
				for (var i = (int)(e.Delta.Y - 0.5); i < 0; ++i)
				{
					if (session.ZoomOutCommand.TryExecute())
						zoomed = true;
				}
			}
			e.Handled = zoomed;
		}


		// Called when property changed.
		protected override void OnPropertyChanged<T>(AvaloniaPropertyChangedEventArgs<T> change)
		{
			base.OnPropertyChanged(change);
			var property = change.Property;
			if (property == DataContextProperty)
			{
				if (change.OldValue.Value is Session oldSession)
				{
					oldSession.PropertyChanged -= this.OnSessionPropertyChanged;
					oldSession.OpenSourceFileCommand.CanExecuteChanged -= this.OnSessionCommandCanExecuteChanged;
					oldSession.ResetBrightnessAdjustmentCommand.CanExecuteChanged -= this.OnSessionCommandCanExecuteChanged;
					oldSession.ResetContrastAdjustmentCommand.CanExecuteChanged -= this.OnSessionCommandCanExecuteChanged;
					oldSession.SaveAsNewProfileCommand.CanExecuteChanged -= this.OnSessionCommandCanExecuteChanged;
					oldSession.SaveFilteredImageCommand.CanExecuteChanged -= this.OnSessionCommandCanExecuteChanged;
					oldSession.SaveRenderedImageCommand.CanExecuteChanged -= this.OnSessionCommandCanExecuteChanged;
				}
				if (change.NewValue.Value is Session newSession)
				{
					// attach to session
					newSession.PropertyChanged += this.OnSessionPropertyChanged;
					newSession.OpenSourceFileCommand.CanExecuteChanged += this.OnSessionCommandCanExecuteChanged;
					newSession.ResetBrightnessAdjustmentCommand.CanExecuteChanged += this.OnSessionCommandCanExecuteChanged;
					newSession.ResetContrastAdjustmentCommand.CanExecuteChanged += this.OnSessionCommandCanExecuteChanged;
					newSession.SaveAsNewProfileCommand.CanExecuteChanged += this.OnSessionCommandCanExecuteChanged;
					newSession.SaveFilteredImageCommand.CanExecuteChanged += this.OnSessionCommandCanExecuteChanged;
					newSession.SaveRenderedImageCommand.CanExecuteChanged += this.OnSessionCommandCanExecuteChanged;
					this.canOpenSourceFile.Update(newSession.OpenSourceFileCommand.CanExecute(null));
					this.canResetBrightnessAndContrastAdjustment.Update(newSession.ResetBrightnessAdjustmentCommand.CanExecute(null)
						|| newSession.ResetContrastAdjustmentCommand.CanExecute(null));
					this.canSaveAsNewProfile.Update(newSession.SaveAsNewProfileCommand.CanExecute(null));
					this.canSaveImage.Update(newSession.SaveFilteredImageCommand.CanExecute(null)
						|| newSession.SaveRenderedImageCommand.CanExecute(null));
					this.canShowEvaluateImageDimensionsMenu.Update(newSession.IsSourceFileOpened);

					// setup histograms panel
					Grid.SetColumnSpan(this.imageViewerGrid, newSession.IsRenderingParametersPanelVisible ? 1 : 3);
					this.renderingParamsPanelColumn.Width = new GridLength(newSession.RenderingParametersPanelSize, GridUnitType.Pixel);

					// update rendered image
					this.updateEffectiveRenderedImageAction.Schedule();
					this.updateEffectiveRenderedImageIntModeAction.Schedule();
				}
				else
				{
					this.canOpenSourceFile.Update(false);
					this.canResetBrightnessAndContrastAdjustment.Update(false);
					this.canSaveAsNewProfile.Update(false);
					this.canSaveImage.Update(false);
					this.canShowEvaluateImageDimensionsMenu.Update(false);
					this.updateEffectiveRenderedImageAction.Execute();
				}
				this.keepHistogramsVisible = false;
				this.keepRenderingParamsPanelVisible = false;
				this.UpdateEffectiveRenderedImageScale();
				this.updateStatusBarStateAction.Schedule();
			}
			else if (property == EffectiveRenderedImageProperty)
				this.UpdateEffectiveRenderedImageScale();
        }


        // Called when CanExecute of command of session has been changed.
        void OnSessionCommandCanExecuteChanged(object? sender, EventArgs e)
		{
			if (!(this.DataContext is Session session))
				return;
			if (sender == session.OpenSourceFileCommand)
				this.canOpenSourceFile.Update(session.OpenSourceFileCommand.CanExecute(null));
			else if (sender == session.ResetBrightnessAdjustmentCommand
				|| sender == session.ResetContrastAdjustmentCommand)
			{
				this.canResetBrightnessAndContrastAdjustment.Update(session.ResetBrightnessAdjustmentCommand.CanExecute(null)
					|| session.ResetContrastAdjustmentCommand.CanExecute(null));
			}
			else if (sender == session.SaveAsNewProfileCommand)
				this.canSaveAsNewProfile.Update(session.SaveAsNewProfileCommand.CanExecute(null));
			else if (sender == session.SaveFilteredImageCommand
				|| sender == session.SaveRenderedImageCommand)
			{
				this.canSaveImage.Update(session.SaveFilteredImageCommand.CanExecute(null)
					|| session.SaveRenderedImageCommand.CanExecute(null));
			}
		}


		// Called when property of session changed.
		void OnSessionPropertyChanged(object? sender, PropertyChangedEventArgs e)
		{
			if (sender is not Session session)
				return;
			switch (e.PropertyName)
			{
				case nameof(Session.EffectiveRenderedImageScale):
                    {
						if (!session.FitRenderedImageToViewport)
                        {
							var viewportSize = this.imageScrollViewer.Viewport;
							var viewportOffset = this.imageScrollViewer.Offset;
							var contentSize = this.imageScrollViewer.Extent;
							var centerX = (viewportOffset.X + viewportSize.Width / 2) / contentSize.Width;
							var centerY = (viewportOffset.Y + viewportSize.Height / 2) / contentSize.Height;
							this.targetImageViewportCenter = new Vector(centerX, centerY);
						}
						this.UpdateEffectiveRenderedImageScale();
					}
					break;
				case nameof(Session.FitRenderedImageToViewport):
					{
						// [Workaround] rearrange scroll viewer of the image viewer
						var padding = this.imageScrollViewer.Padding;
						this.imageScrollViewer.Padding = new Thickness(-1);
						this.imageScrollViewer.Padding = padding;
						this.targetImageViewportCenter = new Vector(0.5, 0.5);
						break;
					}
				case nameof(Session.HasRenderingError):
				case nameof(Session.InsufficientMemoryForRenderedImage):
					this.updateStatusBarStateAction.Schedule();
					break;
				case nameof(Session.IsHistogramsVisible):
					if (session.IsHistogramsVisible)
						this.keepHistogramsVisible = true;
					break;
				case nameof(Session.IsRenderingParametersPanelVisible):
					if (session.IsRenderingParametersPanelVisible)
					{
						Grid.SetColumnSpan(this.imageViewerGrid, 1);
						this.keepRenderingParamsPanelVisible = true;
					}
					else
						Grid.SetColumnSpan(this.imageViewerGrid, 3);
					break;
				case nameof(Session.IsSourceFileOpened):
					this.canShowEvaluateImageDimensionsMenu.Update((sender as Session)?.IsSourceFileOpened ?? false);
					this.updateStatusBarStateAction.Schedule();
					break;
				case nameof(Session.IsZooming):
					if (session.IsZooming)
						this.StartUsingSmallRenderedImage();
					else if (!this.stopUsingSmallRenderedImageAction.IsScheduled)
						this.stopUsingSmallRenderedImageAction.Execute();
					break;
				case nameof(Session.QuarterSizeRenderedImage):
				case nameof(Session.RenderedImage):
					this.updateEffectiveRenderedImageAction.Execute();
					break;
			}
		}


		// Called when setting changed.
		void OnSettingChanged(object? sender, SettingChangedEventArgs e)
		{
			if (e.Key == SettingKeys.ShowProcessInfo)
				this.SetValue<bool>(ShowProcessInfoProperty, (bool)e.Value);
		}


		// Called when test button clicked.
		void OnTestButtonClick()
		{
			this.Application.Restart(AppSuiteApplication.RestoreMainWindowsArgument);
		}


		// Called when property of window changed.
		void OnWindowPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
		{
			var property = e.Property;
			if (property == Avalonia.Controls.Window.HeightProperty 
				|| property == Avalonia.Controls.Window.WidthProperty)
			{
				this.StartUsingSmallRenderedImage();
				this.stopUsingSmallRenderedImageAction.Reschedule(StopUsingSmallRenderedImageDelay);
			}
			else if (property == Avalonia.Controls.Window.WindowStateProperty)
			{
				if ((WindowState)e.OldValue.AsNonNull() == WindowState.Maximized 
					|| (WindowState)e.NewValue.AsNonNull() == WindowState.Maximized)
				{
					this.StartUsingSmallRenderedImage();
					this.stopUsingSmallRenderedImageAction.Reschedule(StopUsingSmallRenderedImageDelay);
				}
			}
		}


		// Open brightness and contrast adjustment UI.
		void OpenBrightnessAndContrastAdjustmentPopup() => this.brightnessAndContrastAdjustmentPopup.Open();


		// Open color adjustment UI.
		void OpenColorAdjustmentPopup() => this.colorAdjustmentPopup.Open();


		// Open source file.
		async void OpenSourceFile()
		{
			// check state
			if (!this.canOpenSourceFile.Value)
			{
				Logger.LogError("Cannot open source file in current state");
				return;
			}

			// find window
			var window = this.FindAncestorOfType<Avalonia.Controls.Window>();
			if (window == null)
			{
				Logger.LogError("No window to show open file dialog");
				return;
			}

			// select file
			var fileName = (await new OpenFileDialog().ShowAsync(window)).Let((it) =>
			{
				if (it != null && it.IsNotEmpty())
					return it[0];
				return null;
			});
			if (fileName == null)
				return;

			// open file
			this.OpenSourceFile(fileName);
		}
		void OpenSourceFile(string fileName)
		{
			// check state
			if (!(this.DataContext is Session session))
			{
				Logger.LogError("No session to open source file");
				return;
			}
			var command = session.OpenSourceFileCommand;
			if (!command.CanExecute(fileName))
			{
				Logger.LogError("Cannot change source file in current state");
				return;
			}

			// open file
			command.Execute(fileName);
		}


		/// <summary>
		/// <see cref="ICommand"/> to open source file.
		/// </summary>
		public ICommand OpenSourceFileCommand { get; }


		// Reset brightness and contrast.
		void ResetBrightnessAndContrastAdjustment()
        {
			// check state
			if (!this.canResetBrightnessAndContrastAdjustment.Value)
				return;

			// reset
			if (this.DataContext is Session session)
			{
				session.ResetBrightnessAdjustmentCommand.TryExecute();
				session.ResetContrastAdjustmentCommand.TryExecute();
			}
        }


		// Command to reset brightness and contrast.
		ICommand ResetBrightnessAndContrastAdjustmentCommand { get; }


		// Save as new profile.
		async void SaveAsNewProfile()
		{
			// check state
			if (!(this.DataContext is Session session))
			{
				Logger.LogError("No session to save as new profile");
				return;
			}
			if (!this.canSaveAsNewProfile.Value || !session.SaveAsNewProfileCommand.CanExecute(null))
			{
				Logger.LogError("Cannot save as new profile in current state");
				return;
			}

			// find window
			var window = this.FindAncestorOfType<Avalonia.Controls.Window>();
			if (window == null)
			{
				Logger.LogError("No window to show dialog");
				return;
			}

			// get name
			var name = session.GenerateNameForNewProfile();
			while (true)
			{
				// input name
				name = await new TextInputDialog()
				{
					InitialText = name,
					Message = this.Application.GetString("SessionControl.InputNameOfProfile"),
				}.ShowDialog(window);
				if (string.IsNullOrWhiteSpace(name))
					return;

				// check name
				if (ImageRenderingProfiles.ValidateNewUserDefinedProfileName(name))
					break;

				// show message for duplicate name
				await new MessageDialog()
				{
					Icon = MessageDialogIcon.Warning,
					Message = string.Format(this.Application.GetStringNonNull("SessionControl.DuplicateNameOfProfile"), name),
				}.ShowDialog(window);
			}

			// save as new profile
			session.SaveAsNewProfileCommand.Execute(name);
		}


		/// <summary>
		/// <see cref="ICommand"/> to save parameters as new profile.
		/// </summary>
		public ICommand SaveAsNewProfileCommand { get; }


		// Save image to file.
		async void SaveImage()
		{
			// check state
			if (this.DataContext is not Session session)
			{
				Logger.LogError("No session to save rendered image");
				return;
			}
			if (!this.canSaveImage.Value)
				return;

			// find window
			var window = this.FindAncestorOfType<Avalonia.Controls.Window>();
			if (window == null)
			{
				Logger.LogError("No window to show dialog");
				return;
			}

			// select image to save
			var saveFilteredImage = false;
			if (session.IsFilteringRenderedImageNeeded)
			{
				var result = await new MessageDialog()
				{
					Buttons = MessageDialogButtons.YesNoCancel,
					Icon = MessageDialogIcon.Question,
					Message = this.Application.GetString("SessionControl.ConfirmSavingFilteredImage")
				}.ShowDialog(window);
				if (result == MessageDialogResult.Cancel)
					return;
				saveFilteredImage = (result == MessageDialogResult.Yes);
			}

			// select file
			var fileName = await new SaveFileDialog().Also((dialog) =>
			{
				var app = (App)this.Application;
				dialog.Filters.Add(new FileDialogFilter().Also((filter) =>
				{
					filter.Name = app.GetString("FileType.Jpeg");
					filter.Extensions.Add("jpg");
					filter.Extensions.Add("jpeg");
					filter.Extensions.Add("jpe");
					filter.Extensions.Add("jfif");
				}));
				dialog.Filters.Add(new FileDialogFilter().Also((filter) =>
				{
					filter.Name = app.GetString("FileType.Png");
					filter.Extensions.Add("png");
				}));
				dialog.Filters.Add(new FileDialogFilter().Also((filter) =>
				{
					filter.Name = app.GetString("FileType.RawBgra");
					filter.Extensions.Add("bgra");
				}));
				dialog.InitialFileName = session.SourceFileName?.Let(it => Path.GetFileNameWithoutExtension(it) + ".jpg") ?? $"Export_{session.ImageWidth}x{session.ImageHeight}.jpg";
			}).ShowAsync(window);
			if (fileName == null)
				return;

			// check format
			var fileFormat = (Media.FileFormat?)null;
			if (Media.FileFormats.TryGetFormatsByFileName(fileName, out var fileFormats))
				fileFormat = fileFormats.First();

			// setup parameters
			var parameters = new Session.ImageSavingParams();
			if (fileFormat == Media.FileFormats.Jpeg)
			{
				var jpegOptions = await new JpegImageEncodingOptionsDialog().ShowDialog<Media.ImageEncoders.ImageEncodingOptions?>(window);
				if (jpegOptions == null)
					return;
				parameters.Options = jpegOptions.Value;
			}
			parameters.FileName = fileName;

			// find encoder
			if (fileFormat != null && Media.ImageEncoders.ImageEncoders.TryGetEncoderByFormat(fileFormat, out var encoder))
				parameters.Encoder = encoder;

			// save
			if (saveFilteredImage)
				session.SaveFilteredImageCommand.TryExecute(parameters);
			else
				session.SaveRenderedImageCommand.TryExecute(parameters);
		}


		/// <summary>
		/// <see cref="ICommand"/> to save image to file.
		/// </summary>
		public ICommand SaveImageCommand { get; }


		// Scroll given point of image scroll viewer to specific position of viewport.
		void ScrollImageScrollViewer(Vector contentPosition, Vector viewportPosition)
		{
			var viewportSize = this.imageScrollViewer.Viewport;
			var contentSize = this.imageScrollViewer.Extent;
			var offsetX = (contentSize.Width * contentPosition.X) - (viewportSize.Width * viewportPosition.X);
			var offsetY = (contentSize.Height * contentPosition.Y) - (viewportSize.Height * viewportPosition.Y);
			if (offsetX < 0)
				offsetX = 0;
			else if (offsetX + viewportSize.Width > contentSize.Width)
				offsetX = contentSize.Width - viewportSize.Width;
			if (offsetY < 0)
				offsetY = 0;
			else if (offsetY + viewportSize.Height > contentSize.Height)
				offsetY = contentSize.Height - viewportSize.Height;
			this.imageScrollViewer.Offset = new Vector(offsetX, offsetY);
		}


		// Show application info.
		void ShowAppInfo()
        {
			this.FindLogicalAncestorOfType<Avalonia.Controls.Window>()?.Let(async (window) =>
			{
				using var appInfo = new AppInfo();
				await new ApplicationInfoDialog(appInfo).ShowDialog(window);
			});
        }


		// Show application options.
		void ShowAppOptions() => this.ShowAppOptions(ApplicationOptionsDialogSection.First);
		void ShowAppOptions(ApplicationOptionsDialogSection initSection)
		{
			this.FindLogicalAncestorOfType<Avalonia.Controls.Window>()?.Let(async (window) =>
			{
				switch (await new ApplicationOptionsDialog() { InitialFocusedSection = initSection }.ShowDialog<ApplicationOptionsDialogResult>(window))
				{
					case ApplicationOptionsDialogResult.RestartApplicationNeeded:
						Logger.LogWarning("Need to restart application");
						if (this.Application.IsDebugMode)
							this.Application.Restart($"{App.DebugArgument} {App.RestoreMainWindowsArgument}");
						else
							this.Application.Restart(App.RestoreMainWindowsArgument);
						break;
					case ApplicationOptionsDialogResult.RestartMainWindowsNeeded:
						Logger.LogWarning("Need to restart main windows");
						this.Application.RestartMainWindows();
						break;
				}
			});
		}


		// Show color space management settings in application options.
		void ShowColorSpaceManagementOptions() => this.ShowAppOptions(ApplicationOptionsDialogSection.ColorSpaceManagement);


		/// <summary>
		/// <see cref="ICommand"/> to show menu of image dimensions evaluation.
		/// </summary>
		public ICommand ShowEvaluateImageDimensionsMenuCommand { get; }


		// Show file actions.
		void ShowFileActions()
		{
			if (this.fileActionsMenu.PlacementTarget == null)
				this.fileActionsMenu.PlacementTarget = this.fileActionsButton;
			this.fileActionsMenu.Open(this.fileActionsButton);
		}


		// Show other actions.
		void ShowOtherActions()
		{
			if (this.otherActionsMenu.PlacementTarget == null)
				this.otherActionsMenu.PlacementTarget = this.otherActionsButton;
			this.otherActionsMenu.Open(this.otherActionsButton);
		}


		// Show process info on UI or not.
		bool ShowProcessInfo { get => this.GetValue<bool>(ShowProcessInfoProperty); }


		// Show file in file explorer.
		void ShowSourceFileInFileExplorer()
        {
			if (!CarinaStudio.Platform.IsOpeningFileManagerSupported)
				return;
			if (this.DataContext is not Session session)
				return;
			var fileName = session.SourceFileName;
			if (!string.IsNullOrEmpty(fileName))
				CarinaStudio.Platform.OpenFileManager(fileName);
		}


		// Start using small rendered image.
		void StartUsingSmallRenderedImage()
		{
			if (this.DataContext is not Session session)
				return;
			if (!session.FitRenderedImageToViewport && session.EffectiveRenderedImageScale >= 2)
				return;
			this.stopUsingSmallRenderedImageAction.Cancel();
			if (!this.useSmallRenderedImage)
			{
				this.useSmallRenderedImage = true;
				this.updateEffectiveRenderedImageAction.Schedule();
				this.updateEffectiveRenderedImageIntModeAction.Schedule();
			}
		}


		// Status bar state.
		StatusBarState StatusBarState { get => this.GetValue<StatusBarState>(StatusBarStateProperty); }


		// Update effective scale of rendered image.
		void UpdateEffectiveRenderedImageScale()
		{
			// get session
			if (this.DataContext is not Session session)
				return;

			// get base scale
			var scale = session.EffectiveRenderedImageScale;

			// apply screen DPI
			session.RenderedImage?.Let((renderedImage) =>
			{
				this.FindAncestorOfType<Avalonia.Controls.Window>()?.Let((window) =>
				{
					var screenDpi = window.Screens.Primary.PixelDensity;
					scale *= (Math.Min(renderedImage.Dpi.X, renderedImage.Dpi.Y) / 96.0 / screenDpi);
				});
			});

			// apply scaling for quarter size image
			if (this.EffectiveRenderedImage == session.QuarterSizeRenderedImage)
				scale *= 2;

			// update
			if (Math.Abs(this.EffectiveRenderedImageScale - scale) > 0.0001)
				this.SetValue<double>(EffectiveRenderedImageScaleProperty, scale);
			this.updateEffectiveRenderedImageIntModeAction.Schedule();
		}
	}
}
