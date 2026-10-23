using UnityEngine;

public static class NeuroTapSessionReminderService
{
    public static void ApplyReminderPreference(bool enabled)
    {
        if (enabled)
        {
            Debug.Log("NeuroTap session reminders are enabled. Platform-specific Quest/Meta notification scheduling should be connected here.");
            return;
        }

        Debug.Log("NeuroTap session reminders are disabled. Platform-specific Quest/Meta notification cancellation should be connected here.");
    }
}
