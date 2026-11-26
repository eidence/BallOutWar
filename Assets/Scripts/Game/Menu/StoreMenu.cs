using System;
using BOW.Meta;
using UnityEngine;
using UnityEngine.UI;

namespace BOW.Game
{
    /// <summary>
    /// Store screen (US-8.1, US-8.5, US-8.7, US-8.8). At launch it sells one thing, the Modes unlock; skins join
    /// it once their art exists. Prices come from Google Play; purchases go through Google's own sheet.
    /// </summary>
    public sealed class StoreMenu
    {
        private readonly RectTransform _root;
        private readonly Action _back;
        private readonly MenuButton _buy;
        private readonly Text _buyLabel, _status, _owned;

        public StoreMenu(Transform canvas, Action back)
        {
            _back = back;
            _root = UiFactory.Stretch("Store", canvas);
            var box = MenuStyle.ScreenBox(_root, "STORE", out _);

            var card = UiFactory.Rect("Modes unlock", box, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 10f), new Vector2(1100f, 560f));
            MenuStyle.Panel(card, MenuStyle.Gold);
            MenuStyle.Heading(UiFactory.Rect("Name", card, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -40f), new Vector2(1000f, 80f)),
                "MODES UNLOCK", 60, MenuStyle.Gold);
            var body = UiFactory.Text(UiFactory.Rect("Body", card, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -140f), new Vector2(960f, 250f)),
                "Unlocks Elimination now, and every new game mode we add later.\n\n" +
                "One payment, no ads, no currencies.\n" +
                "When you host, everyone in your lobby can play your modes.",
                32, MenuStyle.TextMuted, TextAnchor.UpperCenter, outline: false);
            body.horizontalOverflow = HorizontalWrapMode.Wrap;

            _buy = MenuStyle.Make(card, "Unlock", MenuIcon.Store, MenuStyle.Kind.Primary, new Vector2(0.5f, 0f), new Vector2(0f, 90f), new Vector2(560f, 110f),
                () => PurchaseService.Buy(Products.ModesUnlock));
            _buyLabel = _buy.Label;
            _owned = UiFactory.Text(UiFactory.Rect("Owned", card, new Vector2(0.5f, 0f), new Vector2(0.5f, 0.5f), new Vector2(0f, 90f), new Vector2(600f, 80f)),
                "OWNED - thank you!", 44, new Color(0.4f, 1f, 0.6f), TextAnchor.MiddleCenter, outline: false);
            UiFactory.UseDisplayFont(_owned);

            _status = UiFactory.Text(UiFactory.Rect("Status", box, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 170f), new Vector2(1500f, 50f)),
                "", 30, MenuStyle.Gold, TextAnchor.MiddleCenter, outline: false);
            MenuStyle.BackButton(box, "Back", Close);
            MenuStyle.Make(box, "Restore purchases", null, MenuStyle.Kind.Quiet, new Vector2(1f, 0f), new Vector2(-260f, 80f), new Vector2(420f, 96f), PurchaseService.Restore);

            PurchaseService.Changed += Refresh;
            EntitlementStore.Current.Changed += Refresh;
            _root.gameObject.SetActive(false);
        }

        public void Show(string reason = null)
        {
            if (PurchaseService.State == StoreState.NotStarted || PurchaseService.State == StoreState.Unavailable) PurchaseService.Initialize();
            if (reason != null) PurchaseService.Message = reason;
            _root.gameObject.SetActive(true);
            Refresh();
        }

        public void Hide() => _root.gameObject.SetActive(false);

        public void Close()
        {
            PurchaseService.Message = null;
            Hide();
            _back();
        }

        public void Dispose()
        {
            PurchaseService.Changed -= Refresh;
            EntitlementStore.Current.Changed -= Refresh;
        }

        private void Refresh()
        {
            if (!_root.gameObject.activeSelf) return;
            bool owned = EntitlementStore.Current.Owns(Products.ModesUnlock);
            _owned.gameObject.SetActive(owned);
            _buy.gameObject.SetActive(!owned);

            string price = PurchaseService.PriceOf(Products.ModesUnlock);
            switch (PurchaseService.State)
            {
                case StoreState.Ready:
                    _buy.Interactable = price != null;
                    _buyLabel.text = price != null ? $"Unlock for {price}" : "Not available";
                    break;
                case StoreState.Connecting:
                case StoreState.NotStarted:
                    _buy.Interactable = false;
                    _buyLabel.text = "Loading...";
                    break;
                default:
                    _buy.Interactable = false;
                    _buyLabel.text = "Store unavailable offline";
                    break;
            }
            _status.text = PurchaseService.Message ?? "";
        }
    }
}
