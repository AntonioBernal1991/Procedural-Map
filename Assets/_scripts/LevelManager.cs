using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Simple run/level progression manager:
/// - You assign a list of UI Buttons (one per run).
/// - Locked runs: button not interactable, text color = red.
/// - Unlocked runs: interactable, text color = green.
///
/// By default only run 0 is unlocked. When a run is finished with "good finish", the next run unlocks.
/// </summary>
[DisallowMultipleComponent]
public class LevelManager : MonoBehaviour
{
    public static LevelManager Instance { get; private set; }

    [Header("Run Buttons (index = runIndex)")]
    [SerializeField] private List<Button> runButtons = new List<Button>();

    [Header("Colors (button TEXT)")]
    [SerializeField] private Color lockedTextColor = new Color(1f, 0f, 0f, 1f);   // red
    [SerializeField] private Color unlockedTextColor = new Color(0f, 1f, 0f, 1f); // green

    [Header("Debug")]
    [SerializeField] private bool log = false;

    private int _unlockedUpToIndex = 0;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    private void Start()
    {
        RefreshButtons();
    }

    private void OnValidate()
    {
        // Keep UI consistent while editing.
        if (!Application.isPlaying)
        {
            // Don't create listeners in edit mode, just refresh visuals.
            RefreshButtons();
        }
    }

    public bool IsUnlocked(int runIndex)
    {
        return runIndex >= 0 && runIndex <= _unlockedUpToIndex;
    }

    /// <summary>
    /// Call this when the current run ends. Only unlocks the next run if goodFinish==true.
    /// </summary>
    public void ReportRunFinished(int runIndex, bool goodFinish)
    {
        if (runIndex < 0) return;

        if (goodFinish)
        {
            // Unlock next run (linear progression).
            _unlockedUpToIndex = Mathf.Max(_unlockedUpToIndex, runIndex + 1);
            if (log) Debug.Log($"[LevelManager] Good finish on run {runIndex}. Unlocked up to {_unlockedUpToIndex}", this);
        }
        else
        {
            if (log) Debug.Log($"[LevelManager] Run {runIndex} finished WITHOUT good finish. No new unlock.", this);
        }

        RefreshButtons();
    }

    private void RefreshButtons()
    {
        for (int i = 0; i < runButtons.Count; i++)
        {
            Button b = runButtons[i];
            if (b == null) continue;

            bool unlocked = IsUnlocked(i);

            b.interactable = unlocked;

            Color textColor;
            if (!unlocked) textColor = lockedTextColor;
            else textColor = unlockedTextColor;

            if (!TrySetButtonTextColor(b, textColor) && log)
            {
                Debug.LogWarning($"[LevelManager] Could not find a text Graphic under button '{b.name}' to recolor.", b);
            }
        }
    }

    private static bool TrySetButtonTextColor(Button button, Color color)
    {
        if (button == null) return false;

        // Prefer legacy UI Text if present.
        Text legacy = button.GetComponentInChildren<Text>(true);
        if (legacy != null)
        {
            legacy.color = color;
            return true;
        }

        // Fallback: find any Graphic under the button that isn't the button's own background image.
        // (TextMeshProUGUI also derives from Graphic, so this covers it without referencing TMPro.)
        Graphic bg = button.targetGraphic;
        Graphic[] graphics = button.GetComponentsInChildren<Graphic>(true);
        for (int i = 0; i < graphics.Length; i++)
        {
            Graphic g = graphics[i];
            if (g == null) continue;
            if (g == bg) continue; // don't recolor the background
            if (g is Image) continue; // likely other images/icons
            g.color = color;
            return true;
        }

        return false;
    }
}

