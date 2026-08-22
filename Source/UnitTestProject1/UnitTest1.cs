using System.Collections.Generic;
using System.Threading.Tasks;
using EsuEcosMiddleman.ECoS;
using EsuEcosMiddleman.Extensions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace UnitTestProject1
{
    [TestClass]
    public class UnitTest1
    {
        [TestMethod]
        public void ActionTriggerNormalizer_MapsAccessoryAddressStateAndPort()
        {
            var cmd = CommandFactory.Create("set(201, state[1])");

            var match = ActionTriggerNormalizer.TryCreate(cmd, out var trigger);

            Assert.IsTrue(match);
            Assert.AreEqual(201, trigger.Address);
            Assert.AreEqual(1, trigger.Port);
            Assert.AreEqual("on", trigger.State);
        }

        [TestMethod]
        public void ActionDispatcher_InvokesMatchingMappingOnly()
        {
            var action = new RecordingAction();
            var dispatcher = new ActionDispatcher(new List<ActionMapping>
            {
                new ActionMapping
                {
                    Name = "DayLight",
                    Trigger = new ActionTrigger { Address = 201, State = "on" },
                    Action = action
                }
            });

            var cmd = CommandFactory.Create("set(201, state[1])");
            dispatcher.DispatchAsync(cmd, "set(201, state[1])").GetAwaiter().GetResult();

            Assert.IsNotNull(action.ExecutedContext);
            Assert.AreEqual("DayLight", action.ExecutedContext.MappingName);
            Assert.AreEqual(201, action.ExecutedContext.Trigger.Address);
        }

        [TestMethod]
        public void ActionDispatcher_DoesNothingWithoutMatch()
        {
            var action = new RecordingAction();
            var dispatcher = new ActionDispatcher(new List<ActionMapping>
            {
                new ActionMapping
                {
                    Name = "Night",
                    Trigger = new ActionTrigger { Address = 202, State = "on" },
                    Action = action
                }
            });

            var cmd = CommandFactory.Create("set(201, state[1])");
            dispatcher.DispatchAsync(cmd, "set(201, state[1])").GetAwaiter().GetResult();

            Assert.IsNull(action.ExecutedContext);
        }

        private class RecordingAction : IAction
        {
            public ActionContext ExecutedContext { get; private set; }

            public Task ExecuteAsync(ActionContext context)
            {
                ExecutedContext = context;
                return Task.CompletedTask;
            }
        }
    }
}
