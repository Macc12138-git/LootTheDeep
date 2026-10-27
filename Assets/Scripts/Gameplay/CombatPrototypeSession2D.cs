using System.Text;
using LootTheDeep.Inventory;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace LootTheDeep.Gameplay
{
    public sealed class CombatPrototypeSession2D : MonoBehaviour
    {
        [SerializeField] private CombatHealth2D player;
        [SerializeField] private Text healthLabel;
        [SerializeField] private Image healthFill;
        [SerializeField] private Text extractionLabel;
        [SerializeField] private GameObject resultPanel;
        [SerializeField] private Text resultTitle;
        [SerializeField] private Text resultItems;
        [SerializeField] private Button restartButton;

        private InventorySession inventory;
        private float previousTimeScale = 1f;
        private bool focusPaused;
        private bool hasFocus = true;
        private bool applicationPaused;
        public bool IsFinished { get; private set; }

        public void Configure(CombatHealth2D target, Text hp, Image fill, Text exit,
            GameObject result, Text title, Text items, Button restart)
        {
            player = target; healthLabel = hp; healthFill = fill; extractionLabel = exit;
            resultPanel = result; resultTitle = title; resultItems = items; restartButton = restart;
        }

        private void Start()
        {
            inventory = FindFirstObjectByType<InventorySession>();
            if (player != null)
            {
                player.Changed += RefreshHealth;
                player.Died += OnPlayerDied;
                RefreshHealth(player);
            }
            if (resultPanel != null) resultPanel.SetActive(false);
            if (restartButton != null) restartButton.onClick.AddListener(Restart);
            SetExtractionStatus(null, 0f);
        }

        private void RefreshHealth(CombatHealth2D target)
        {
            if (healthLabel != null) healthLabel.text = $"生命  {target.CurrentHealth} / {target.MaxHealth}";
            if (healthFill != null) healthFill.fillAmount = (float)target.CurrentHealth / target.MaxHealth;
        }

        public void SetExtractionStatus(string label, float progress)
        {
            if (extractionLabel != null)
                extractionLabel.text = IsFinished ? "" : label == null ? "" :
                    progress <= 0f ? label : $"{label}  {Mathf.RoundToInt(progress * 100f)}%";
        }

        private void OnPlayerDied(CombatHealth2D target) => Finish(false);

        public void Finish(bool extracted)
        {
            if (IsFinished) return;
            IsFinished = true;
            inventory?.Close();
            if (inventory != null) inventory.enabled = false;
            if (player != null) player.GetComponent<UnderwaterMover2D>()?.SetCombatEnded(true);
            int count = 0;
            var names = new StringBuilder();
            if (inventory != null)
                foreach (GridInventory grid in inventory.Storage)
                    foreach (LootItem item in grid.Items)
                    {
                        if (count < 5) names.AppendLine(item.Definition.displayName);
                        count++;
                    }
            if (resultTitle != null) resultTitle.text = extracted ? "已完成测试撤离" : "行动失败";
            if (resultItems != null)
                resultItems.text = extracted
                    ? $"本局带出 {count} 件物资（仅测试结算，尚未入库）\n{names}"
                    : inventory == null
                        ? "你已失去行动能力。\n按 Enter 或点击下方按钮重新开始。"
                        : $"本局未撤出 {count} 件携带物资。\n重新开始可再试一次。";
            if (resultPanel != null) resultPanel.SetActive(true);
            previousTimeScale = Time.timeScale;
            Time.timeScale = 0f;
        }

        public void Restart()
        {
            Time.timeScale = 1f;
            SceneManager.LoadScene(gameObject.scene.name);
        }

        private void Update()
        {
            if (IsFinished && Keyboard.current?.enterKey.wasPressedThisFrame == true) Restart();
        }

        private void OnApplicationFocus(bool focused)
        {
            hasFocus = focused;
            UpdateFocusPause();
        }

        private void OnApplicationPause(bool paused)
        {
            applicationPaused = paused;
            UpdateFocusPause();
        }

        private void UpdateFocusPause()
        {
            if (IsFinished || Application.isBatchMode) return;
            if ((!hasFocus || applicationPaused) && !focusPaused)
            {
                previousTimeScale = Time.timeScale;
                Time.timeScale = 0f;
                focusPaused = true;
            }
            else if (hasFocus && !applicationPaused && focusPaused)
            {
                Time.timeScale = previousTimeScale;
                focusPaused = false;
            }
        }

        private void OnDestroy()
        {
            if (player != null) { player.Changed -= RefreshHealth; player.Died -= OnPlayerDied; }
            if (restartButton != null) restartButton.onClick.RemoveListener(Restart);
            if (focusPaused || IsFinished) Time.timeScale = previousTimeScale;
        }
    }
}
