using System;
using System.Collections.Generic;
using System.IO;
using Hardcodet.Wpf.TaskbarNotification;
using Microsoft.Win32;
using Newtonsoft.Json;
using SupporTik.Classes;

namespace SupporTik.Services
{
	public class StorageService
	{
		private const string DefaultFileName = "keybinds.json";

		private readonly string _folderPath;

		public StorageService()
		{
			// Путь к AppData\Roaming\SupporTik
			string appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
			_folderPath = Path.Combine(appDataPath, "SupporTik");
		}

		#region Работа с локальным файлом (AppData)

		public void SaveData<T>(List<T> data, string fileName = DefaultFileName)
		{
			try
			{
				if (!Directory.Exists(_folderPath))
				{
					Directory.CreateDirectory(_folderPath);
				}

				string filePath = Path.Combine(_folderPath, fileName);
				string jsonString = JsonConvert.SerializeObject(data, Formatting.Indented);
				File.WriteAllText(filePath, jsonString);
			}
			catch (Exception ex)
			{
				Console.WriteLine($"Ошибка при сохранении данных: {ex.Message}");
			}
		}

		public List<T> LoadData<T>(string fileName = DefaultFileName)
		{
			try
			{
				string filePath = Path.Combine(_folderPath, fileName);
				if (!File.Exists(filePath))
				{
					return new List<T>();
				}

				string jsonString = File.ReadAllText(filePath);
				return JsonConvert.DeserializeObject<List<T>>(jsonString) ?? new List<T>();
			}
			catch (Exception ex)
			{
				Console.WriteLine($"Ошибка при загрузке данных: {ex.Message}");
				return new List<T>();
			}
		}

		#endregion

		#region Экспорт / Импорт

		public void ExportData()
		{
			try
			{
				var saveFileDialog = new SaveFileDialog
				{
					Filter = "JSON Files (*.json)|*.json",
					DefaultExt = "json",
					FileName = "SupporTik_Export.json",
					Title = "Экспорт биндов, групп и настроек"
				};

				if (saveFileDialog.ShowDialog() == true)
				{
					var package = new ExportPackage
					{
						Binds = App._bindKeys,
						Groups = App._groupInfos,
						Settings = new ExportSettings
						{
							StartMinimized = Properties.Settings.Default.StartMinimized,
							MinimizeToTray = Properties.Settings.Default.MinimizeToTray,
							SelectedKey = Properties.Settings.Default.SelectedKey,
							SelectedModifiers = Properties.Settings.Default.SelectedModifiers
						}
					};

					string json = JsonConvert.SerializeObject(package, Formatting.Indented);
					File.WriteAllText(saveFileDialog.FileName, json);

					App._notifyIcon?.ShowBalloonTip(
						"Экспорт",
						"Бинды, группы и настройки успешно сохранены!",
						BalloonIcon.None);
				}
			}
			catch (Exception)
			{
				App._notifyIcon?.ShowBalloonTip(
					"Экспорт",
					"Произошла ошибка при экспорте!",
					BalloonIcon.None);
			}
		}

		public void ImportData()
		{
			try
			{
				var openFileDialog = new OpenFileDialog
				{
					Filter = "JSON Files (*.json)|*.json",
					DefaultExt = "json",
					Title = "Выберите файл для импорта"
				};

				if (openFileDialog.ShowDialog() != true)
				{
					return;
				}

				string json = File.ReadAllText(openFileDialog.FileName);
				ExportPackage package = null;

				try
				{
					package = JsonConvert.DeserializeObject<ExportPackage>(json);
				}
				catch (JsonException)
				{
					// Старый формат экспорта — просто список биндов, без групп и настроек.
					// package остаётся null, ниже сработает запасной путь.
				}

				List<BindKeys> importedBinds = package?.Binds
					?? JsonConvert.DeserializeObject<List<BindKeys>>(json);

				if (importedBinds == null)
				{
					throw new InvalidOperationException("Файл не похож на экспорт SupporTik");
				}

				App._bindKeys = importedBinds;
				SaveData(App._bindKeys);

				if (package?.Groups != null)
				{
					App._groupInfos = package.Groups;
					SaveData(App._groupInfos, "groups.json");
				}

				if (package?.Settings != null)
				{
					Properties.Settings.Default.StartMinimized = package.Settings.StartMinimized;
					Properties.Settings.Default.MinimizeToTray = package.Settings.MinimizeToTray;
					Properties.Settings.Default.SelectedKey = package.Settings.SelectedKey;
					Properties.Settings.Default.SelectedModifiers = package.Settings.SelectedModifiers;
					Properties.Settings.Default.Save();
				}

				// Перерегистрируем хоткеи под импортированные бинды/настройки
				App.RegisterDefaultHotkeys();

				App._notifyIcon?.ShowBalloonTip(
					"Импорт",
					"Данные успешно импортированы!",
					BalloonIcon.None);
			}
			catch (Exception)
			{
				App._notifyIcon?.ShowBalloonTip(
					"Импорт",
					"Произошла ошибка при импорте!",
					BalloonIcon.None);
			}
		}

		#endregion
	}
}