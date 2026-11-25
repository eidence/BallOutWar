using System;
using System.Collections.Generic;
using BOW.Meta;
using UnityEngine;
using UnityEngine.Purchasing;

namespace BOW.Game
{
    public enum StoreState
    {
        NotStarted,
        Connecting,
        Ready,
        /// <summary>No store connection (offline, or no Google Play). Owned items still work from the cache (US-8.1).</summary>
        Unavailable,
    }

    /// <summary>
    /// Google Play purchases through Unity IAP 5 (TDD 10.2). Connects in the background at boot, fetches the
    /// catalogue as non-consumables, grants entitlements for confirmed and pending orders (then confirms them so
    /// Google acknowledges within its 3-day window), and restores owned purchases on every start.
    /// In the editor Unity IAP uses its fake store, so purchases can be tested without Google Play.
    /// </summary>
    public static class PurchaseService
    {
        private static StoreController _store;
        private static readonly Dictionary<string, Product> _products = new Dictionary<string, Product>();

        public static StoreState State { get; private set; } = StoreState.NotStarted;

        /// <summary>The last problem worth telling the player, e.g. "Purchase cancelled." Cleared when read by the store screen.</summary>
        public static string Message { get; set; }

        /// <summary>Raised when the state, prices or a purchase result change.</summary>
        public static event Action Changed;

        public static async void Initialize()
        {
            if (State == StoreState.Connecting || State == StoreState.Ready) return;
            State = StoreState.Connecting;
            Changed?.Invoke();
            try
            {
                _store = UnityIAPServices.StoreController();
                _store.OnProductsFetched += OnProductsFetched;
                _store.OnProductsFetchFailed += f => Fail("Couldn't load the store.");
                _store.OnPurchasesFetched += OnPurchasesFetched;
                _store.OnPurchasePending += OnPurchasePending;
                _store.OnPurchaseFailed += OnPurchaseFailed;
                _store.OnPurchaseDeferred += o => { Message = "Purchase pending: it unlocks when the payment completes."; Changed?.Invoke(); };
                _store.OnStoreDisconnected += d => { State = StoreState.Unavailable; Changed?.Invoke(); };

                await _store.Connect();
                var definitions = new List<ProductDefinition>();
                foreach (string id in Products.All) definitions.Add(new ProductDefinition(id, ProductType.NonConsumable));
                _store.FetchProducts(definitions);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Store unavailable: {e.Message}");
                Fail(null);
            }
        }

        public static string PriceOf(string productId)
            => _products.TryGetValue(productId, out var p) && p.metadata != null ? p.metadata.localizedPriceString : null;

        /// <summary>Opens Google Play's own purchase sheet (US-8.7). The result arrives through the store events.</summary>
        public static void Buy(string productId)
        {
            if (State != StoreState.Ready || !_products.TryGetValue(productId, out var product) || !product.availableToPurchase)
            {
                Message = "The store isn't available right now. Check your connection and try again.";
                Changed?.Invoke();
                return;
            }
            _store.PurchaseProduct(product);
        }

        /// <summary>Asks the store for everything this account owns (US-8.5).</summary>
        public static void Restore()
        {
            if (State != StoreState.Ready) { Initialize(); return; }
            _store.FetchPurchases();
            Message = "Checking your purchases...";
            Changed?.Invoke();
        }

        private static void OnProductsFetched(List<Product> products)
        {
            foreach (var p in products) _products[p.definition.id] = p;
            State = StoreState.Ready;
            _store.FetchPurchases(); // restores owned purchases on every start
            Changed?.Invoke();
        }

        private static void OnPurchasesFetched(Orders orders)
        {
            foreach (var order in orders.ConfirmedOrders) GrantAll(order);
            foreach (var order in orders.PendingOrders)
            {
                GrantAll(order);
                _store.ConfirmPurchase(order);
            }
            if (Message == "Checking your purchases...") Message = "Purchases restored.";
            Changed?.Invoke();
        }

        private static void OnPurchasePending(PendingOrder order)
        {
            // TODO(TDD 10.2): validate the Google receipt on device with the obfuscated Play public key (Tangle)
            // once the app exists in Play Console. Until then the store's own result is trusted.
            GrantAll(order);
            _store.ConfirmPurchase(order);
            Message = "Unlocked. Thanks for supporting Ball Out War!";
            Changed?.Invoke();
        }

        private static void OnPurchaseFailed(FailedOrder order)
        {
            Message = order.FailureReason == PurchaseFailureReason.UserCancelled ? "Purchase cancelled." : $"Purchase failed ({order.FailureReason}).";
            Changed?.Invoke();
        }

        private static void GrantAll(Order order)
        {
            foreach (var item in order.CartOrdered.Items())
                if (item.Product?.definition != null) EntitlementStore.Current.Grant(item.Product.definition.id);
        }

        private static void Fail(string message)
        {
            State = StoreState.Unavailable;
            if (message != null) Message = message;
            Changed?.Invoke();
        }
    }
}
