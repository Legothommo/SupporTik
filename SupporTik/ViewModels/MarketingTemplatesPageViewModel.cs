using SupporTik.Classes;
using SupporTik.Mvvm;
using SupporTik.Services;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows.Input;

namespace SupporTik.ViewModels
{
	public class MarketingTemplateItemViewModel : ViewModelBase
	{
		private readonly MarketingOfferTextBuilder _builder;
		internal MarketingTextTemplate Model { get; }

		public string Id => Model.Id;
		public string Category => Model.Category;
		public bool IsFavorite => Model.IsFavorite;
		public bool FavoriteFilled => IsFavorite ? true : false;
		public string[] CampaignScopes => MarketingTemplateTags.CampaignScopes;
		public string[] AudienceRoles => MarketingTemplateTags.Roles;
		public string[] OfferTypes => MarketingTemplateTags.OfferTypes;

		public string CampaignScope
		{
			get => Model.CampaignScope;
			set => SetTag(value, () => Model.CampaignScope, newValue => Model.CampaignScope = newValue, nameof(CampaignScope));
		}

		public string AudienceRole
		{
			get => Model.AudienceRole;
			set => SetTag(value, () => Model.AudienceRole, newValue => Model.AudienceRole = newValue, nameof(AudienceRole));
		}

		public string OfferType
		{
			get => Model.OfferType;
			set => SetTag(value, () => Model.OfferType, newValue => Model.OfferType = newValue, nameof(OfferType));
		}

		public string Name
		{
			get => Model.Name;
			set
			{
				if (Model.Name == value) return;
				Model.Name = value;
				OnPropertyChanged();
			}
		}

		public string Content
		{
			get => Model.Content;
			set
			{
				if (Model.Content == value) return;
				Model.Content = value;
				OnPropertyChanged();
				OnPropertyChanged(nameof(Preview));
			}
		}

		public string Preview => _builder.RenderPreview(Model);

		public MarketingTemplateItemViewModel(
			MarketingTextTemplate model,
			MarketingOfferTextBuilder builder)
		{
			Model = model;
			_builder = builder;
		}

		public void RefreshFavorite()
		{
			OnPropertyChanged(nameof(IsFavorite));
			OnPropertyChanged(nameof(FavoriteFilled));
		}

		private void SetTag(
			string value,
			Func<string> getter,
			Action<string> setter,
			string propertyName)
		{
			if (getter() == value || string.IsNullOrEmpty(value)) return;
			setter(value);
			Model.IsFavorite = false;
			OnPropertyChanged(propertyName);
			OnPropertyChanged(nameof(Category));
			RefreshFavorite();
		}
	}

	public class MarketingTemplatesPageViewModel : ViewModelBase
	{
		private readonly MarketingTemplateService _service;
		private readonly MarketingOfferTextBuilder _builder;
		private readonly INotificationService _notifications;

		public ObservableCollection<MarketingTemplateItemViewModel> Templates { get; } =
			new ObservableCollection<MarketingTemplateItemViewModel>();

		private MarketingTemplateItemViewModel _selectedTemplate;
		public MarketingTemplateItemViewModel SelectedTemplate
		{
			get => _selectedTemplate;
			set => SetProperty(ref _selectedTemplate, value);
		}

		public ICommand SaveCommand { get; }
		public ICommand AddCommand { get; }
		public ICommand DuplicateCommand { get; }
		public ICommand DeleteCommand { get; }
		public ICommand FavoriteCommand { get; }

		public MarketingTemplatesPageViewModel(
			MarketingTemplateService service,
			INotificationService notifications)
		{
			_service = service;
			_notifications = notifications;
			_builder = new MarketingOfferTextBuilder(service);
			SaveCommand = new RelayCommand(Save);
			AddCommand = new RelayCommand(Add);
			DuplicateCommand = new RelayCommand(Duplicate, () => SelectedTemplate != null);
			DeleteCommand = new RelayCommand(Delete, () => SelectedTemplate != null);
			FavoriteCommand = new RelayCommand(ToggleFavorite);
			Reload();
		}

		public void Move(MarketingTemplateItemViewModel source, MarketingTemplateItemViewModel target)
		{
			if (source == null || target == null || ReferenceEquals(source, target)) return;
			int oldIndex = Templates.IndexOf(source);
			int newIndex = Templates.IndexOf(target);
			if (oldIndex < 0 || newIndex < 0) return;
			Templates.Move(oldIndex, newIndex);
			_service.SaveOrder(Templates.Select(item => item.Model));
		}

		private void Save()
		{
			_service.Save();
			Reload(SelectedTemplate?.Id);
			_notifications.ShowBalloon("Шаблоны", "Изменения сохранены.", isWarning: false);
		}

		private void Add()
		{
			MarketingTextTemplate template = _service.Add();
			Reload(template.Id);
		}

		private void Duplicate()
		{
			MarketingTextTemplate copy = _service.Duplicate(SelectedTemplate.Model);
			Reload(copy.Id);
		}

		private void Delete()
		{
			try
			{
				_service.Delete(SelectedTemplate.Model);
				Reload();
			}
			catch (InvalidOperationException ex)
			{
				_notifications.ShowBalloon("Шаблоны", ex.Message, isWarning: true);
			}
		}

		private void ToggleFavorite(object parameter)
		{
			if (!(parameter is MarketingTemplateItemViewModel item)) return;
			_service.SetFavorite(item.Model);
			Reload(item.Id);
		}

		private void Reload(string selectedId = null)
		{
			selectedId = selectedId ?? SelectedTemplate?.Id;
			Templates.Clear();
			foreach (MarketingTextTemplate template in _service.GetAll()
				.OrderByDescending(item => item.IsFavorite)
				.ThenBy(item => item.SortOrder))
			{
				Templates.Add(new MarketingTemplateItemViewModel(template, _builder));
			}

			SelectedTemplate = Templates.FirstOrDefault(item => item.Id == selectedId) ?? Templates.FirstOrDefault();
		}
	}
}
