using PackRat.Helpers;
using Xunit;

namespace PackRat.Logic.Tests;

public sealed class ReflectionUtilsTests
{
    [Fact]
    public void TryGetAllListLikeMembers_DoesNotInvokeUnrelatedPropertyGetters()
    {
        var inventory = new InventoryShape();

        var lists = ReflectionUtils.TryGetAllListLikeMembers(inventory);

        Assert.Single(lists);
        Assert.Same(inventory.HotbarSlots, lists[0]);
        Assert.Equal(0, inventory.UnrelatedGetterCalls);
    }

    [Fact]
    public void PrewarmReadableMembers_DoesNotInvokePropertyGetter()
    {
        var inventory = new InventoryShape();

        ReflectionUtils.PrewarmReadableMembers(inventory.GetType(), nameof(InventoryShape.SelectedSlotIndex));

        Assert.Equal(0, inventory.SelectedSlotGetterCalls);
        Assert.Equal(3, ReflectionUtils.TryGetFieldOrProperty(inventory,
            nameof(InventoryShape.SelectedSlotIndex)));
        Assert.Equal(1, inventory.SelectedSlotGetterCalls);
    }

    private sealed class InventoryShape
    {
        public List<object> HotbarSlots { get; } = new List<object>();
        public int UnrelatedGetterCalls { get; private set; }
        public int SelectedSlotGetterCalls { get; private set; }

        public object UnrelatedState
        {
            get
            {
                UnrelatedGetterCalls++;
                return new object();
            }
        }

        public int SelectedSlotIndex
        {
            get
            {
                SelectedSlotGetterCalls++;
                return 3;
            }
        }
    }
}
