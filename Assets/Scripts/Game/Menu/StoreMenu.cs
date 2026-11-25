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
        private readonly Button _buy;
        private readonly Text _buyLabel, _status, _owned;

        public StoreMenu(Transform canvas, Action back)
        {
            _back = back;
            _root = UiFactory.Stretch("Store", canvas);
            UiFactory.Image(_root, new Color(0f, 0f, 0f, 0.35f));
            var box = UiFactory.Rect("Box", _root, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1640f, 960f));
            UiFactory.Image(box, MenuTheme.Panel);
            UiFactory.Text(UiFactory.Rect("Title", box, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -20f), new Vector2(1000f, 90f)),
                "STORE", 72, MenuTheme.Cyan);

            var card = UiFactory.Rect("Modes unlock", box, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 30f), new Vector2(1100f, 560f));
            UiFactory.Image(card, new Color(0.07f, 0.1f, 0.16f, 0.95f));
            var stripe = UiFactory.Rect("Stripe", card, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(1100f, 10f));
            UiFactory.Image(stripe, MenuTheme.Cyan);
            UiFactory.Text(UiFactory.Rect("Name", card, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -40f), new Vector2(1000f, 80f)),
                "MODES UNLOCK", 60, Color.white);
            var body = UiFactory.Text(UiFactory.Rect("Body", card, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -140f), new Vector2(960f, 250f)),
                "Unlocks Elimination now, and every new game mode we add later.\n\n" +
                "One payment, no ads, no currencies.\n" +
                "When you host, everyone in your lobby can play your modes.",
                32, new Color(1f, 1f, 1f, 0.85f), TextAnchor.UpperCenter);
            body.horizontalOverflow = HorizontalWrapMode.Wrap;

            _buy = UiFactory.Button(card, "", new Vector2(0.5f, 0f), new Vector2(0f, 90f), new Vector2(480f, 110f), MenuTheme.ButtonGreen,
                () => PurchaseService.Buy(Products.ModesUnlock));
            _buyLabel = _buy.GetComponentInChildren<Text>();
            _owned = UiFactory.Text(UiFactory.Rect("Owned", card, new Vector2(0.5f, 0f), new Vector2(0.5f, 0.5f), new Vector2(0f, 90f), new Vector2(600f, 80f)),
                "OWNED - thank you!", 44, new Color(0.4f, 1f, 0.6f));

            _status = UiFactory.Text(UiFactory.Rect("Status", box, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 170f), new Vector2(1500f, 50f)),
                "", 30, new Color(1f, 0.8f, 0.5f));
            UiFactory.Button(box, "Back", new Vector2(0f, 0f), new Vector2(240f, 80f), new Vector2(320f, 100f), MenuTheme.ButtonGrey, Close);
            UiFactory.Button(box, "Restore purchases", new Vector2(1f, 0f), new Vector2(-270f, 80f), new Vector2(420f, 100f), MenuTheme.ButtonGrey, PurchaseService.Restore);

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
                    _buy.interactable = price != null;
                    _buyLabel.text = price != null ? $"Unlock for {price}" : "Not available";
                    break;
                case StoreState.Connecting:
                case StoreState.NotStarted:
                    _buy.interactable = false;
                    _buyLabel.text = "Loading...";
                    break;
                default:
                    _buy.interactable = false;
                    _buyLabel.text = "Store unavailable offline";
                    break;
            }
            _status.text = PurchaseService.Message ?? "";
        }
    }
}
