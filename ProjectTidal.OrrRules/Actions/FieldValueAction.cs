namespace ProjectTidal.OrrRules.Actions {
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.RegularExpressions;
    using OrderDynamics.Core.BusinessFacade;
    using OrderDynamics.Core.BusinessLogic;

    public class FieldValueAction : OrderRoutingActionBase {
        private const string TargetFieldRegex = "InventoryLocation\\.StateBag\\[\".+\"\\]";

        public FieldValueAction()  {
        }

        public FieldValueAction(string connectionString) : base(connectionString) {
        }

        public FieldValueAction(IOrderRoutingAction callingAction)
            : base(callingAction) {
        }

        public FieldValueAction(IOrderRoutingAction callingAction, string connectionString)
            : base(callingAction, connectionString) {
        }


        /// <summary>
        /// Inventory location IDs member
        /// </summary>
        [OrderRoutingActionUIHint(OrderRoutingActionUIHintType.InventoryLocationIds)]
        [OrderRoutingActionProperty]
        [OrderRoutingActionUIName("Inventory Location Ids")]
        public string InventoryLocations {
            get; set;
        }

        [OrderRoutingActionUIHint(OrderRoutingActionUIHintType.None)]
        [OrderRoutingActionProperty]
        [OrderRoutingActionUIName("Target Field (i.e. InventoryLocation.StateBag[\"FillRate\"])")]
        public string TargetField {
            get;
            set;
        }

        [OrderRoutingActionUIHint(OrderRoutingActionUIHintType.Enum)]
        [OrderRoutingActionProperty]
        [OrderRoutingActionUIName("Prefer High/Low Values")]
        public ValuePreferenceType ValuePreference {
            get;
            set;
        }

        protected override IOrderRoutingActionResult CreateActionResult(OrderInfo orderInfo, OrderDetails orderDetails) {
            var locations = _inventortyLocationsParser.ParseById(InventoryLocations).ToArray();

            if (!locations.Any()) {
                return new OrderRoutingActionResult {
                    OrderDetailsId = orderDetails.OrderDetailId,
                    OrderRoutingActionAllocationResults = null
                };
            }

            InventoryStatus inventoryStatus = _inventoryService.GetRealtimeInventory(orderDetails.ItemId);
            var orderDetailsLocationInventories = inventoryStatus == null
                ? new List<ItemLocationInventory>()
                : inventoryStatus.LocationInventories;

            var availableLocations =
                (from location in locations
                 join inventoryLocation in orderDetailsLocationInventories
                     on location.Id equals inventoryLocation.InventoryLocationId
                 where inventoryLocation.AvailableInventory > 0
                 select location).ToArray();

            if (!availableLocations.Any()) {
                availableLocations = locations;
            }

            var actionResult = new OrderRoutingActionResult {
                OrderDetailsId = orderDetails.OrderDetailId,
                OrderRoutingActionAllocationResults = new List<IOrderRoutingActionAllocationResult>()
            };

            string key = ExtractStateBagKey();

            var locationsWithKey = availableLocations.Where(a => a.StateBag.ContainsKey(key));

            foreach (var availableLocation in locationsWithKey) {
                double order = 0;

                string stateBagValue = availableLocation.StateBag[key].ToString();

                if (!string.IsNullOrEmpty(stateBagValue)) {
                    double.TryParse(stateBagValue, out order);
                }

                if (ValuePreference == ValuePreferenceType.High) {
                    order *= -1;
                }

                ItemLocationInventory itemLocationInventory =
                    orderDetailsLocationInventories.FirstOrDefault(li => li.InventoryLocationId == availableLocation.Id);
                actionResult.OrderRoutingActionAllocationResults.Add(new OrderRoutingActionAllocationResult {
                    LocationId = availableLocation.Id,
                    Order = order,
                    AvailableInventory = itemLocationInventory?.AvailableInventory ?? 0
                });
            }

            return actionResult;
        }

        protected override void SetWeights(IOrderRoutingActionResult actionResult) {

            SetWeightsHelper(
                actionResult
                , ar => ar.OrderRoutingActionAllocationResults.Min(r => r.Order)
                , ar => ar.OrderRoutingActionAllocationResults.Max(r => r.Order)
                , ar => ar.Order
                , false
                );

        }

        public override bool Validate(out string[] messages) {
            var isValid = true;
            var errorMessages = new List<string>();

            if (String.IsNullOrEmpty(InventoryLocations)) {
                errorMessages.Add("No Inventory Location(s) have been assigned to this action.");
                isValid = false;
            }

            var message = _inventortyLocationsParser.Validate(InventoryLocations);
            if (!string.IsNullOrEmpty(message)) {
                errorMessages.Add(message);
                isValid = false;
            }

            if (string.IsNullOrEmpty(TargetField)) {
                errorMessages.Add("No target field specified");
                isValid = false;
            } else {
                // For this prototype, we will be limited to the InventoryLocation StateBag
                Regex targetRegex = new Regex(TargetFieldRegex);
                if (!targetRegex.IsMatch(TargetField)) {
                    isValid = false;
                    errorMessages.Add("The target field specified is not in the expected format");
                }
            }

            messages = errorMessages.ToArray();
            return isValid;
        }

        private string ExtractStateBagKey() {
            const string startMatch = "InventoryLocation.StateBag[\"";
            const string endMatch = "\"]";

            int start = TargetField.IndexOf(startMatch, StringComparison.OrdinalIgnoreCase) + startMatch.Length;
            int end = TargetField.IndexOf(endMatch, StringComparison.OrdinalIgnoreCase);

            return TargetField.Substring(start, end - start);

        }
    }
}
