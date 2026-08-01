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
					FileName = "SupporTik_Binds.json",
					Title = "Экспорт биндов"
				};

				if (saveFileDialog.ShowDialog() == true)
				{
					string json = JsonConvert.SerializeObject(App._bindKeys, Formatting.Indented);
					File.WriteAllText(saveFileDialog.FileName, json);

					App._notifyIcon?.ShowBalloonTip(
						"Экспорт",
						"Бинды успешно сохранены!",
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
					Title = "Выберите файл биндов"
				};

				if (openFileDialog.ShowDialog() == true)
				{
					string json = File.ReadAllText(openFileDialog.FileName);
					var importedBinds = JsonConvert.DeserializeObject<List<BindKeys>>(json);

					if (importedBinds != null)
					{
						App._bindKeys = importedBinds;
						SaveData(App._bindKeys); // Сохраняем импортированные бинды локально

						App._notifyIcon?.ShowBalloonTip(
							"Импорт",
							"Бинды успешно импортированы!",
							BalloonIcon.None);
					}
				}
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