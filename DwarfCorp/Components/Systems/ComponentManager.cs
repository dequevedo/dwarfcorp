using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace DwarfCorp
{
    /// <summary>
    /// Handles components. All game objects (dwarves, trees, lamps, ravenous wolverines) are just a 
    /// collection of components. Together, the collection is called an 'entity'. Components form a 
    /// tree. Each component has a parent and 0 to N children.
    /// </summary>
    public class ComponentManager
    {
        public class ComponentSaveData
        {
            public List<GameComponent> SaveableComponents;
            public uint RootComponent;
        }

        private Dictionary<uint, GameComponent> Components;
        private uint MaxGlobalID = 0;
        public const int InvalidID = 0;
        // Double-buffered to avoid `new List<>` per frame in AddRemove(). A/B swap: producers
        // write to the "active" side under lock; consumer (main-thread AddRemove) flips and
        // drains its local copy without reallocating.
        private List<GameComponent> _removalsActive = new List<GameComponent>();
        private List<GameComponent> _removalsDraining = new List<GameComponent>();
        private List<GameComponent> _additionsActive = new List<GameComponent>();
        private List<GameComponent> _additionsDraining = new List<GameComponent>();

        public GameComponent RootComponent { get; private set; }

        public void SetRootComponent(GameComponent Component)
        {
            RootComponent = Component;
        }

        public int NumComponents()
        {
            return Components.Count;
        }

        // Replaced legacy Mutex (kernel object, ~microseconds per Wait/Release) with plain
        // monitor locks — these queues are only contended across in-process threads.
        private readonly object _additionLock = new object();
        private readonly object _removalLock = new object();

        public WorldManager World { get; set; }

        public ComponentSaveData GetSaveData()
        {
            // Just in case the root was tagged unserializable for whatever reason.
            RootComponent.SetFlag(GameComponent.Flag.ShouldSerialize, true);

            foreach (var component in Components)
                component.Value.PrepareForSerialization();

            var serializableComponents = Components.Where(c => c.Value.IsFlagSet(GameComponent.Flag.ShouldSerialize)).Select(c => c.Value).ToList();

            return new ComponentSaveData
            {
                SaveableComponents = serializableComponents,
                RootComponent = RootComponent.GlobalID
            };
        }

        /// <summary>
        /// Must be called after serialization to avoid leaking references to dead components.
        /// </summary>
        public void CleanupSaveData()
        {
            foreach (var component in Components)
                component.Value.SerializableChildren = null;
        }

        private void StartThreads()
        {
            for (var i = 0; i < 4; ++i)
            {
                //var updateThread = new System.Threading.Thread(EntityTransformUpdateThread);
                //updateThread.Start();
            }
        }

        public ComponentManager(ComponentSaveData SaveData, WorldManager World)
        {
            this.World = World;
            World.ComponentManager = this;
            Components = new Dictionary<uint, GameComponent>();
            SaveData.SaveableComponents.RemoveAll(c => c == null);

            foreach (var component in SaveData.SaveableComponents)
            {
                Components.Add(component.GlobalID, component);
                component.World = World;
            }

            RootComponent = Components[SaveData.RootComponent] as GameComponent;

            foreach (var component in Components)
                World.ModuleManager.ComponentCreated(component.Value);

            MaxGlobalID = Components.Aggregate<KeyValuePair<uint, GameComponent>, uint>(0, (current, component) => Math.Max(current, component.Value.GlobalID));

            foreach (var component in SaveData.SaveableComponents)
                component.PostSerialization(this);

            foreach (var component in SaveData.SaveableComponents)
            {
                component.CreateCosmeticChildren(this);
                component.HasMoved = true;
                //component.ProcessTransformChange();
            }

            RootComponent.ProcessTransformChange();

            var removals = SaveData.SaveableComponents.Where(p => !p.Parent.HasValue() && p != RootComponent).ToList();

            foreach(var component in removals)
            {
                Console.Error.WriteLine("Component {0} has no parent. removing.", component.Name);
                RemoveComponentImmediate(component);
                SaveData.SaveableComponents.Remove(component);
            }

            StartThreads();
        }

        public ComponentManager(WorldManager state)
        {
            World = state;
            Components = new Dictionary<uint, GameComponent>();
            StartThreads();
        }

        // Fase C.3: reusable scratch HashSet for FindRootBodiesInsideScreenRectangle.
        // Previously allocated a fresh HashSet every call (plus `ToList` at the end
        // allocating a List). Called on every drag-select and hover-tooltip, so this
        // was a meaningful source of Gen 0 pressure in interactive sessions.
        // Clearing a HashSet is O(n) but keeps the backing buckets — no allocation.
        private readonly HashSet<GameComponent> _findRootBodiesScratch = new HashSet<GameComponent>();

        public List<GameComponent> FindRootBodiesInsideScreenRectangle(Rectangle selectionRectangle, Camera camera)
        {
            if (World.Renderer.SelectionBuffer == null)
                return new List<GameComponent>();

            _findRootBodiesScratch.Clear();
            foreach (uint id in World.Renderer.SelectionBuffer.GetIDsSelected(selectionRectangle))
            {
                GameComponent component;
                if (!Components.TryGetValue(id, out component))
                    continue;

                if (!component.IsVisible) continue; // Then why was it drawn in the selection buffer??

                if (component.GetRoot().GetComponent<GameComponent>().HasValue(out var toAdd))
                    _findRootBodiesScratch.Add(toAdd); // HashSet.Add is idempotent — no need for a Contains() check first.
            }

            // The caller receives ownership of the returned List, so this allocation
            // can't be avoided without changing the API signature. But we allocate it
            // at final size (no doubling) and the interim HashSet is no longer
            // per-call.
            var result = new List<GameComponent>(_findRootBodiesScratch.Count);
            foreach (var gc in _findRootBodiesScratch) result.Add(gc);
            return result;
        }

        private object _msgLock = new object();
        private List<KeyValuePair<GameComponent, Message> > _msgList = new List<KeyValuePair<GameComponent, Message> >();

        // Allows components to receive messages recursively while in a thread.
        public void ReceiveMessageLater(GameComponent component, Message msg)
        {
            lock (_msgLock)
            {
                _msgList.Add(new KeyValuePair<GameComponent, Message>(component, msg));
            }
        }

        public void AddComponent(GameComponent component)
        {
            lock (_additionLock)
            {
                MaxGlobalID += 1;
                component.GlobalID = MaxGlobalID;
                _additionsActive.Add(component);
            }
        }

        public void RemoveComponent(GameComponent component)
        {
            lock (_removalLock)
            {
                _removalsActive.Add(component);
            }
        }

        public bool HasComponent(uint id)
        {
            if (Components.ContainsKey(id)) return true;
            lock (_additionLock)
            {
                for (int i = 0; i < _additionsActive.Count; i++)
                    if (_additionsActive[i].GlobalID == id) return true;
            }
            return false;
        }

        private void RemoveComponentImmediate(GameComponent Component)
        {
            if (!Components.ContainsKey(Component.GlobalID))
                return;

            Components.Remove(Component.GlobalID);

            World.ModuleManager.ComponentDestroyed(Component);

            foreach (var child in new List<GameComponent>(Component.EnumerateChildren()))
                RemoveComponentImmediate(child);
        }

        private void AddComponentImmediate(GameComponent Component)
        {
            if (Components.ContainsKey(Component.GlobalID))
            {
                if (Object.ReferenceEquals(Components[Component.GlobalID], Component)) return;
                throw new InvalidOperationException("Attempted to add component with same ID as existing component.");
            }

            Components[Component.GlobalID] = Component;

            World.ModuleManager.ComponentCreated(Component);

            // Todo: Works if we remove this?
            Component.ProcessTransformChange();
        }

        public void FindComponentsToUpdate(HashSet<GameComponent> Into)
        {
            var playerPoint = World.Renderer.Camera.Position;
            World.EnumerateIntersectingRootEntitiesLoose(playerPoint, GameSettings.Current.EntityUpdateDistance, Into);
        }

        public void Update(DwarfTime gameTime, ChunkManager chunks, HashSet<GameComponent> ComponentsToUpdate)
        {
            PerformanceMonitor.PushFrame("Component Update");
            PerformanceMonitor.SetMetric("COMPONENTS", NumComponents());

            var i = 0;
            foreach (var body in ComponentsToUpdate)
            {
                i += 1;
                body.Update(gameTime, chunks, World.Renderer.Camera);
            }

            PerformanceMonitor.SetMetric("ENTITIES UPDATED", i);
            PerformanceMonitor.PopFrame();

            AddRemove();
            ReceiveMessage(gameTime);
        }

        private void ReceiveMessage(DwarfTime Time)
        {
            lock (_msgLock)
            {
                foreach (var msg in _msgList)
                {
                    msg.Key.ReceiveMessageRecursive(msg.Value, Time);
                }
                _msgList.Clear();
            }
        }

        private void AddRemove()
        {
            // Swap the A/B buffers instead of allocating a fresh list each frame.
            // Producers keep writing to the "active" side; we drain the "draining" side.
            List<GameComponent> toAdd;
            lock (_additionLock)
            {
                toAdd = _additionsActive;
                _additionsActive = _additionsDraining;
                _additionsDraining = toAdd;
            }
            for (int i = 0; i < toAdd.Count; i++)
                AddComponentImmediate(toAdd[i]);
            toAdd.Clear();

            List<GameComponent> toRemove;
            lock (_removalLock)
            {
                toRemove = _removalsActive;
                _removalsActive = _removalsDraining;
                _removalsDraining = toRemove;
            }
            for (int i = 0; i < toRemove.Count; i++)
                RemoveComponentImmediate(toRemove[i]);
            toRemove.Clear();
        }

        public void UpdatePaused(DwarfTime gameTime, ChunkManager chunks, Camera camera)
        {
            PerformanceMonitor.PushFrame("Component Update");

            foreach (var component in Components.Values)
                component.UpdatePaused(gameTime, chunks, camera);

            PerformanceMonitor.PopFrame();
            
            AddRemove();
        }

        public MaybeNull<GameComponent> FindComponent(uint ID)
        {
            if (Components.TryGetValue(ID, out GameComponent result))
                return result;
            else
                return null;
        }
    }
}
