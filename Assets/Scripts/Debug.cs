using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using System;
using System.Reflection;
using System.IO;
 
public class Print : EditorWindow
{
    static Type _LogEntriesType;
    static Type _logEntryType;
 
    static MethodInfo _GetCountMethod;
    static MethodInfo _StartGettingEntriesMethod;
    static MethodInfo _GetEntryInternalMethod;
    static MethodInfo _EndGettingEntriesMethod;
 
    static FieldInfo _conditionField;
 
    static bool _isSupport = false;
 
    bool _detail = false;
 
    [MenuItem("Debug/Export Console")]
    static void ShowEditor()
    {
        Print editor = EditorWindow.GetWindowWithRect<Print>(new Rect(-1, -1, 170, 60), true, "Export Console", true);
        editor.Show();
    }
 
    [MenuItem("Debug/Export Console", validate = true)]
    static bool ExportConsoleMenuValidate()
    {
        return _isSupport && GetEntryCount() > 0;
    }
 
    private void OnGUI()
    {
        _detail = EditorGUILayout.Toggle("Detail", _detail);
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("Export"))
        {
            if (DoExportConsole(_detail))
            {
                Close();
            }
        }
        GUILayout.Space(5);
    }
 
 
    static bool DoExportConsole(bool detail)
    {
        string[] logs = GetConsoleEntries();
        string path = EditorUtility.SaveFilePanel("Export Console", Application.dataPath, "ConsoleLog", "txt");
        if (string.IsNullOrEmpty(path))
        {
            return false;
        }
 
        if (!detail)
        {
            for (int i = 0; i < logs.Length; ++i)
            {
                using (var sr = new StringReader(logs[i]))
                {
                    logs[i] = sr.ReadLine();
                }
            }
        }
        File.WriteAllLines(path, logs);
        EditorUtility.OpenWithDefaultApp(path);
        return true;
    }
 
    static Print()
    {
        _LogEntriesType = Type.GetType("UnityEditor.LogEntries,UnityEditor");
        if (_LogEntriesType != null)
        {
            _GetCountMethod = _LogEntriesType.GetMethod("GetCount", BindingFlags.Static | BindingFlags.Public);
            _StartGettingEntriesMethod = _LogEntriesType.GetMethod("StartGettingEntries", BindingFlags.Static | BindingFlags.Public);
            _GetEntryInternalMethod = _LogEntriesType.GetMethod("GetEntryInternal", BindingFlags.Static | BindingFlags.Public);
            _EndGettingEntriesMethod = _LogEntriesType.GetMethod("EndGettingEntries", BindingFlags.Static | BindingFlags.Public);
        }
 
        _logEntryType = Type.GetType("UnityEditor.LogEntry,UnityEditor");
        if (_logEntryType != null)
        {
            _conditionField = _logEntryType.GetField("message", BindingFlags.Public | BindingFlags.Instance);
        }
        CheckSupport();
    }
 
    static void CheckSupport()
    {
        if (_LogEntriesType == null ||
            _logEntryType == null ||
            _GetCountMethod == null ||
            _StartGettingEntriesMethod == null ||
            _GetEntryInternalMethod == null ||
            _EndGettingEntriesMethod == null ||
            _conditionField == null)
        {
            _isSupport = false;
        }
        else
        {
            _isSupport = true;
        }
    }
 
    static string[] GetConsoleEntries()
    {
        if (!_isSupport)
        {
            return null;
        }
        List<string> entries = new List<string>();
 
        object countObj = _GetCountMethod.Invoke(null, null);
 
        _StartGettingEntriesMethod.Invoke(null, null);
 
        int count = int.Parse(countObj.ToString());
        for (int i = 0; i < count; ++i)
        {
            object logEntry = Activator.CreateInstance(_logEntryType);
            object result = _GetEntryInternalMethod.Invoke(null, new object[] { i, logEntry });
            if (bool.Parse(result.ToString()))
            {
                entries.Add(_conditionField.GetValue(logEntry).ToString());
            }
        }
        _EndGettingEntriesMethod.Invoke(null, null);
        return entries.ToArray();
    }
 
    static int GetEntryCount()
    {
        if (!_isSupport)
        {
            return 0;
        }
        object countObj = _GetCountMethod.Invoke(null, null);
        return int.Parse(countObj.ToString());
    }
}