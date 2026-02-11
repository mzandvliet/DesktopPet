using UnityEngine;
using System.Windows.Automation;
using System.Collections.Generic;

public struct IconData
{
    public string name;
    public Rect bounds;
}

public class DesktopIconMonitor
{
    private AutomationElement _desktopListView;
    public List<IconData> icons = new List<IconData>();

    public bool Start()
    {
        AutomationElement desktop = AutomationElement.RootElement;
        _desktopListView = desktop.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.ClassNameProperty, "SysListView32"));

        if (_desktopListView == null)
        {
            Debug.LogError("Couldn't find desktop icon list");
            return false;
        }

        Automation.AddStructureChangedEventHandler(
            _desktopListView,
            TreeScope.Children,
            OnIconsChanged
        );

        return true;
    }

    public void Stop()
    {
        if (_desktopListView != null)
        {
            Automation.RemoveAllEventHandlers();
        }
    }

    private void OnIconsChanged(object sender, StructureChangedEventArgs e)
    {
        Debug.Log($"Desktop icons changed: {e.StructureChangeType}");
        UpdateIconPositions();
    }

    private void UpdateIconPositions()
    {
        icons.Clear();

        var iconElements = _desktopListView.FindAll(TreeScope.Children,
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem));

        foreach (AutomationElement icon in iconElements)
        {
            icons.Add(new IconData
            {
                name = icon.Current.Name,
                bounds = ToUnity(icon.Current.BoundingRectangle)
            });

            Debug.Log($"Icon: {icon.Current.Name} at {icon.Current.BoundingRectangle}");
        }
    }

    private static UnityEngine.Rect ToUnity(System.Windows.Rect rect)
    {
        int screenHeight = Screen.currentResolution.height;

        return new UnityEngine.Rect(
            (float)rect.X,
            (float)(screenHeight - rect.Y - rect.Height),
            (float)rect.Width,
            (float)rect.Height
        );
    }
}