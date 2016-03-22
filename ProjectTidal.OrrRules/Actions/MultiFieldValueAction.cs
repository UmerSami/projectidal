namespace ProjectTidal.OrrRules.Actions
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.RegularExpressions;
    using OrderDynamics.Core.BusinessFacade;
    using OrderDynamics.Core.BusinessLogic;

    public class MultiFieldValueAction : OrderRoutingActionBase {
        private const string TargetFieldRegex = "InventoryLocation\\.StateBag\\[\".+\"\\]";

        public MultiFieldValueAction() {
        }

        public MultiFieldValueAction(string connectionString) : base(connectionString) {
        }

        public MultiFieldValueAction(IOrderRoutingAction callingAction)
            : base(callingAction) {
        }

        public MultiFieldValueAction(IOrderRoutingAction callingAction, string connectionString)
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
        [OrderRoutingActionUIName("Target Field 1 (i.e. InventoryLocation.StateBag[\"FillRate\"])")]
        public string TargetField1 {
            get;
            set;
        }      

        [OrderRoutingActionUIHint(OrderRoutingActionUIHintType.Enum)]
        [OrderRoutingActionProperty]
        [OrderRoutingActionUIName("Field 1 -Prefer High/Low Values")]
        public ValuePreferenceType Field1ValuePreference {
            get;
            set;
        }


        [OrderRoutingActionUIHint(OrderRoutingActionUIHintType.None)]
        [OrderRoutingActionProperty]
        [OrderRoutingActionUIName("Field 1 Weight")]
        public double Field1Weight {
            get;
            set;
        }


        [OrderRoutingActionUIHint(OrderRoutingActionUIHintType.None)]
        [OrderRoutingActionProperty]
        [OrderRoutingActionUIName("Target Field 2 (i.e. InventoryLocation.StateBag[\"FillRate\"])")]
        public string TargetField2 {
            get;
            set;
        }

        [OrderRoutingActionUIHint(OrderRoutingActionUIHintType.Enum)]
        [OrderRoutingActionProperty]
        [OrderRoutingActionUIName("Field 2 -Prefer High/Low Values")]
        public ValuePreferenceType Field2ValuePreference {
            get;
            set;
        }


        [OrderRoutingActionUIHint(OrderRoutingActionUIHintType.None)]
        [OrderRoutingActionProperty]
        [OrderRoutingActionUIName("Field 2 Weight")]
        public double Field2Weight {
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

            string key1 = ExtractStateBagKey(TargetField1);
            string key2 = ExtractStateBagKey(TargetField2);

            var locationsWithBothKeys = availableLocations.Where(a => a.StateBag.ContainsKey(key1) && a.StateBag.ContainsKey(key2)).ToArray();

            double min1 = locationsWithBothKeys.Min(a=> Convert.ToDouble(a.StateBag[key1]));
            double max1 = locationsWithBothKeys.Max(a => Convert.ToDouble(a.StateBag[key1]));
            double min2 = locationsWithBothKeys.Min(a => Convert.ToDouble(a.StateBag[key2]));            
            double max2 = locationsWithBothKeys.Max(a => Convert.ToDouble(a.StateBag[key2]));


            foreach (var availableLocation in locationsWithBothKeys) {
                double value1 = Convert.ToDouble(availableLocation.StateBag[key1]);
                double value2 = Convert.ToDouble(availableLocation.StateBag[key2]);

                double order1 = (value1 - min1)/(max1 - min1);
                double order2 = (value2 - min2)/(max2 - min2);

                if (Field1ValuePreference == ValuePreferenceType.High) {
                    order1 = 1 - order1;
                }

                if (Field2ValuePreference == ValuePreferenceType.High) {
                    order2 = 1 - order2;
                }

                ItemLocationInventory itemLocationInventory = orderDetailsLocationInventories.FirstOrDefault(li => li.InventoryLocationId == availableLocation.Id);

                actionResult.OrderRoutingActionAllocationResults.Add(new OrderRoutingActionAllocationResult {
                    LocationId = availableLocation.Id,
                    Order = (order1 * Field1Weight) + (order2 * Field2Weight),
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

            if (string.IsNullOrEmpty(TargetField1)) {
                errorMessages.Add("No target field 1 specified");
                isValid = false;
            } else {
                // For this prototype, we will be limited to the InventoryLocation StateBag
                Regex targetRegex = new Regex(TargetFieldRegex);
                if (!targetRegex.IsMatch(TargetField1)) {
                    isValid = false;
                    errorMessages.Add("The target field 1 specified is not in the expected format");
                }
            }

            if (string.IsNullOrEmpty(TargetField2)) {
                errorMessages.Add("No target field 2 specified");
                isValid = false;
            } else {
                // For this prototype, we will be limited to the InventoryLocation StateBag
                Regex targetRegex = new Regex(TargetFieldRegex);
                if (!targetRegex.IsMatch(TargetField2)) {
                    isValid = false;
                    errorMessages.Add("The target field 2 specified is not in the expected format");
                }
            }

            messages = errorMessages.ToArray();
            return isValid;
        }

        private static string ExtractStateBagKey(string field) {
            const string startMatch = "InventoryLocation.StateBag[\"";
            const string endMatch = "\"]";

            int start = field.IndexOf(startMatch, StringComparison.OrdinalIgnoreCase) + startMatch.Length;
            int end = field.IndexOf(endMatch, StringComparison.OrdinalIgnoreCase);

            return field.Substring(start, end - start);

        }
    }
}

