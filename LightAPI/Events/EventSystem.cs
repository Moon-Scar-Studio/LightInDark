using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using LightInDark.Core;

namespace LightInDark.Events
{
    public static class EventSystem
    {
        private class ListenerEntry
        {
            public object Instance;
            public MethodInfo Method;
            public Type EventType;
            public int Priority;
            public bool OnlyHost;
            public bool OnlyMyPlayer;
            public bool Local;
            public bool IsStatic => Method.IsStatic;
        }

        private static readonly Dictionary<Type, List<ListenerEntry>> _listeners = new();
        private static readonly HashSet<object> _attached = new();
        private static readonly HashSet<Assembly> _scannedAssemblies = new();
        private static readonly HashSet<Type> _autoInstantiated = new();
        private static readonly Dictionary<Type, Func<IEvent, object>> _playerAccessorCache = new();
        private static readonly Dictionary<Type, List<ListenerEntry>> _dispatchCache = new();
        private static readonly object _gate = new();
        private static int _knownEventTypeCount;

        /// <summary>
        /// 注册一个程序集扫描其全部静态监听方法，并自动挂载其中实现 IEventListener 的类。
        /// </summary>
        public static void RegisterAssembly(Assembly assembly)
        {
            if (assembly == null) return;

            lock (_gate)
            {
                if (!_scannedAssemblies.Add(assembly)) return;
            }

            int statics = ScanStaticHandlers(assembly);
            int marked = AttachMarkedListeners(assembly);

            SortAllListeners();
            LightLogger.Log($"[EventSystem] {assembly.GetName().Name}: static listner {statics} , marked classes {marked} ");
            LogDiagnostics();
        }

        public static void Attach(object instance)
        {
            if (instance == null) return;

            var type = instance.GetType();
            int count = 0;

            lock (_gate)
            {
                if (!_attached.Add(instance)) return;

                foreach (var method in CollectListenerMethods(type))
                {
                    if (!TryGetEventType(method, out var eventType)) continue;
                    AddEntry(instance, method, eventType);
                    count++;
                }
            }

            SortAllListeners();
            if (count > 0)
            {
                try { LightLogger.Log($"[EventSystem] Attach {type.Name}: {count} 个监听方法"); }
                catch { }
            }
        }

        /// <summary>卸载对象全部监听方法。</summary>
        public static void Detach(object instance)
        {
            if (instance == null) return;

            lock (_gate)
            {
                if (!_attached.Remove(instance)) return;
                foreach (var list in _listeners.Values)
                    list.RemoveAll(e => ReferenceEquals(e.Instance, instance));
            }

            SortAllListeners();
        }

        public static void RegisterInstance(object instance) => Attach(instance);

        /// <summary>Detach 兼容.</summary>
        public static void UnregisterInstance(object instance) => Detach(instance);
        private static int ScanStaticHandlers(Assembly assembly)
        {
            int count = 0;

            foreach (var type in GetLoadableTypes(assembly))
            {
                if (!type.IsClass) continue;

                MethodInfo[] methods;
                try
                {
                    methods = type.GetMethods(BindingFlags.Static | BindingFlags.Public |
                                              BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                }
                catch { continue; }

                foreach (var method in methods)
                {
                    if (method.IsAbstract || method.IsSpecialName) continue;
                    if (!TryGetEventType(method, out var eventType)) continue;

                    lock (_gate)
                    {
                        AddEntry(null, method, eventType);
                    }
                    count++;
                }
            }

            return count;
        }

        private static int AttachMarkedListeners(Assembly assembly)
        {
            int count = 0;

            foreach (var type in GetLoadableTypes(assembly))
            {
                if (!type.IsClass || type.IsAbstract || type.IsInterface) continue;
                if (!typeof(IEventListener).IsAssignableFrom(type)) continue;

                lock (_gate)
                {
                    if (!_autoInstantiated.Add(type)) continue;
                }

                if (type.GetConstructor(Type.EmptyTypes) == null)
                {
                    LightLogger.LogWarning($"[EventSystem] {type.Name} 实现了 IEventListener 但没有无参构造，无法自动挂载");
                    continue;
                }

                try
                {
                    Attach(Activator.CreateInstance(type));
                    count++;
                }
                catch (Exception ex)
                {
                    LightLogger.LogError($"[EventSystem] 自动挂载 {type.Name} 失败", ex);
                }
            }

            return count;
        }
        private static IEnumerable<MethodInfo> CollectListenerMethods(Type type)
        {
            var seen = new HashSet<string>();

            for (var cur = type; cur != null && cur != typeof(object); cur = cur.BaseType)
            {
                MethodInfo[] declared;
                try
                {
                    declared = cur.GetMethods(BindingFlags.Instance | BindingFlags.Public |
                                              BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                }
                catch { continue; }

                foreach (var method in declared)
                {
                    if (method.IsAbstract || method.IsSpecialName) continue;
                    if (!TryGetEventType(method, out var eventType)) continue;
                    if (!seen.Add($"{method.Name}|{eventType.FullName}")) continue;
                    yield return method;
                }
            }
        }

        private static bool TryGetEventType(MethodInfo method, out Type eventType)
        {
            eventType = null;
            var parameters = method.GetParameters();
            if (parameters.Length != 1) return false;

            var candidate = parameters[0].ParameterType;
            if (!typeof(IEvent).IsAssignableFrom(candidate)) return false;

            eventType = candidate;
            return true;
        }

        private static void AddEntry(object instance, MethodInfo method, Type eventType)
        {
            if (!_listeners.TryGetValue(eventType, out var list))
                _listeners[eventType] = list = new List<ListenerEntry>();

            list.Add(new ListenerEntry
            {
                Instance = instance,
                Method = method,
                EventType = eventType,
                Priority = method.GetCustomAttribute<EventPriorityAttribute>()?.Priority ?? 0,
                OnlyHost = method.GetCustomAttribute<OnlyHostAttribute>() != null,
                OnlyMyPlayer = method.GetCustomAttribute<OnlyMyPlayerAttribute>() != null,
                Local = method.GetCustomAttribute<LocalAttribute>() != null
            });
        }

        private static IEnumerable<Type> GetLoadableTypes(Assembly assembly)
        {
            try
            {
                return assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
               
                return ex.Types.Where(t => t != null);
            }
            catch
            {
                return Array.Empty<Type>();
            }
        }

        public static T RunEvent<T>(T ev) where T : IEvent
        {
            try
            {
                // 按优先级降序对"自身 + 所有 IEvent 基类"的监听器统一调度，
                // 支持事件继承：如 ReportDeadBodyEvent / CalledEmergencyMeetingEvent : MeetingPreStartEvent。
                DispatchEvent(typeof(T), ev);
                return ev;
            }
            catch (Exception ex)
            {
                LightLogger.LogError("EventSystem.RunEvent", ex);
                return default(T);
            }
        }

        /// <summary>把事件分发给该类型及其所有 IEvent 基类的监听器。</summary>
        private static void DispatchEvent(Type eventType, IEvent ev)
        {
            List<ListenerEntry> combined;
            lock (_gate)
            {
                if (!_dispatchCache.TryGetValue(eventType, out combined) || combined == null || combined.Count == 0)
                    return;
            }

            bool host = AmongUsClient.Instance?.AmHost ?? false;
            bool client = AmongUsClient.Instance?.AmClient ?? false;
            PlayerControl localPlayer = PlayerControl.LocalPlayer;

            for (int i = 0; i < combined.Count; i++)
            {
                var entry = combined[i];

                // 静态监听直接调用；实例监听需已绑定实例
                if (!entry.IsStatic && entry.Instance == null)
                    continue;

                if (entry.OnlyHost && !host) continue;
                if (entry.Local && !client) continue;
                if (entry.OnlyMyPlayer)
                {
                    if (localPlayer == null) continue;
                    var getter = GetPlayerAccessor(eventType);
                    PlayerControl eventPlayer = null;
                    if (getter != null)
                    {
                        try { eventPlayer = getter(ev) as PlayerControl; }
                        catch { eventPlayer = null; }
                    }
                    if (eventPlayer != localPlayer) continue;
                }

                try
                {
                    entry.Method.Invoke(entry.Instance, new[] { ev });
                }
                catch (Exception ex)
                {
                    LightLogger.LogError($"Event execution error in {entry.Method.Name}: {ex}");
                }
            }
        }

        private static Func<IEvent, object> GetPlayerAccessor(Type type)
        {
            lock (_gate)
            {
                if (_playerAccessorCache.TryGetValue(type, out var cached))
                    return cached;
            }

            var prop = type.GetProperty("Player");
            Func<IEvent, object> result = null;
            if (prop != null && typeof(PlayerControl).IsAssignableFrom(prop.PropertyType))
            {
                var getMethod = prop.GetGetMethod();
                if (getMethod != null)
                {
                    result = ev => { try { return getMethod.Invoke(ev, null); } catch { return null; } };
                }
            }

            lock (_gate)
            {
                _playerAccessorCache[type] = result;
            }
            return result;
        }

        private static void SortAllListeners()
        {
            lock (_gate)
            {
                foreach (var list in _listeners.Values)
                    list.Sort((a, b) => b.Priority.CompareTo(a.Priority));
            }
            RebuildDispatchCache();
        }

        private static void RebuildDispatchCache()
        {
            lock (_gate)
            {
                _dispatchCache.Clear();
                _playerAccessorCache.Clear();
                _knownEventTypeCount = 0;

                foreach (var concreteType in CollectConcreteEventTypes())
                {
                    _knownEventTypeCount++;

                    List<ListenerEntry> combined = null;
                    for (var cur = concreteType; cur != null && typeof(IEvent).IsAssignableFrom(cur); cur = cur.BaseType)
                    {
                        if (_listeners.TryGetValue(cur, out var list) && list.Count > 0)
                        {
                            combined ??= new List<ListenerEntry>();
                            combined.AddRange(list);
                        }
                    }

                    if (combined != null)
                    {
                        combined.Sort((a, b) => b.Priority.CompareTo(a.Priority));
                        _dispatchCache[concreteType] = combined;
                    }
                }
            }
        }
        private static IEnumerable<Type> CollectConcreteEventTypes()
        {
            var seen = new HashSet<Type>();
            var assemblies = _scannedAssemblies.Count > 0
                ? (IEnumerable<Assembly>)_scannedAssemblies
                : new[] { Assembly.GetExecutingAssembly() };

            foreach (var assembly in assemblies)
            foreach (var type in GetLoadableTypes(assembly))
            {
                if (type.IsAbstract || type.IsInterface || !type.IsClass) continue;
                if (!typeof(IEvent).IsAssignableFrom(type)) continue;
                if (seen.Add(type)) yield return type;
            }
        }

        private static void LogDiagnostics()
        {
            lock (_gate)
            {
                int statics = _listeners.Values.Sum(l => l.Count(e => e.IsStatic));
                int instances = _listeners.Values.Sum(l => l.Count(e => !e.IsStatic));
                int listened = _listeners.Count(kv => kv.Value.Count > 0);

                LightLogger.Log($"[EventSystem] 事件类型 {_knownEventTypeCount} \n 有监听 {listened} ；静态监听 {statics}，实例监听 {instances}");
            }
        }
    }
}
