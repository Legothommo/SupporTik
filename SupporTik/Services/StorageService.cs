using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace SupporTik.Services
{
	/// <summary>Чистая JSON-персистентность в AppData\Roaming\SupporTik — без знания о биндах/группах/трее.</summary>
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
	}
}
