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
		public string DataDirectory => _folderPath;

		public StorageService()
		{
			// Путь к AppData\Roaming\SupporTik
			string appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
			_folderPath = Path.Combine(appDataPath, "SupporTik");
		}

		public void SaveData<T>(List<T> data, string fileName = DefaultFileName)
		{
			string tempPath = null;
			try
			{
				if (!Directory.Exists(_folderPath))
				{
					Directory.CreateDirectory(_folderPath);
				}

				string filePath = Path.Combine(_folderPath, fileName);
				tempPath = filePath + ".tmp";
				string backupPath = filePath + ".bak";

				string jsonString = JsonConvert.SerializeObject(data, Formatting.Indented);
				File.WriteAllText(tempPath, jsonString);

				if (File.Exists(filePath))
				{
					// Атомарная замена: подменяет файл и одновременно сохраняет предыдущую
					// версию в backupPath — если запись оборвётся на середине (сбой питания,
					// принудительное завершение процесса), либо останется старый файл, либо
					// появится валидный новый, промежуточного повреждённого состояния не будет
					File.Replace(tempPath, filePath, backupPath);
				}
				else
				{
					File.Move(tempPath, filePath);
				}
			}
			catch (Exception ex)
			{
				LoggingService.LogError($"StorageService.SaveData({fileName})", ex);

				try
				{
					if (!string.IsNullOrEmpty(tempPath) && File.Exists(tempPath))
					{
						File.Delete(tempPath);
					}
				}
				catch (Exception cleanupError)
				{
					LoggingService.LogError($"StorageService.SaveData({fileName}): cleanup", cleanupError);
				}

				throw;
			}
		}

		public List<T> LoadData<T>(string fileName = DefaultFileName)
		{
			string filePath = Path.Combine(_folderPath, fileName);

			List<T> data = TryLoad<T>(filePath, out Exception primaryError);
			if (data != null)
			{
				return data;
			}

			// Основной файл повреждён или отсутствует — пробуем откатиться на бэкап
			// от предыдущего успешного сохранения (см. SaveData)
			string backupPath = filePath + ".bak";
			data = TryLoad<T>(backupPath, out _);

			if (data != null)
			{
				LoggingService.LogError($"StorageService.LoadData({fileName}): восстановлено из {backupPath}", primaryError);
				return data;
			}

			if (primaryError != null)
			{
				LoggingService.LogError($"StorageService.LoadData({fileName}): основной файл и резервная копия недоступны", primaryError);
			}

			return new List<T>();
		}

		private List<T> TryLoad<T>(string filePath, out Exception error)
		{
			error = null;

			try
			{
				if (!File.Exists(filePath))
				{
					return null;
				}

				string jsonString = File.ReadAllText(filePath);
				return JsonConvert.DeserializeObject<List<T>>(jsonString);
			}
			catch (Exception ex)
			{
				error = ex;
				return null;
			}
		}
	}
}
