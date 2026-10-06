using System;
using System.IO;
using UnityEngine;

// guarda y carga el progreso en un archivo json
public static class SaveSystem
{
    static string FilePath => Path.Combine(Application.persistentDataPath, "save.json");

    // guarda los datos en disco
    public static void Save(SaveData data)
    {
        try
        {
            File.WriteAllText(FilePath, JsonUtility.ToJson(data));
        }
        catch (Exception e)
        {
            Debug.LogWarning("SaveSystem: no se pudo guardar. " + e.Message);
        }
    }

    // carga los datos, devuelve null si no hay o fallan
    public static SaveData Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return null;
            return JsonUtility.FromJson<SaveData>(File.ReadAllText(FilePath));
        }
        catch (Exception e)
        {
            Debug.LogWarning("SaveSystem: no se pudo cargar. " + e.Message);
            return null;
        }
    }

    // borra el progreso guardado
    public static void Delete()
    {
        try
        {
            if (File.Exists(FilePath)) File.Delete(FilePath);
        }
        catch (Exception e)
        {
            Debug.LogWarning("SaveSystem: no se pudo borrar. " + e.Message);
        }
    }
}