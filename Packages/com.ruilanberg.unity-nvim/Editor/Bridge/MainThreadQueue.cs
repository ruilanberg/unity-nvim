using System;
using System.Collections.Concurrent;
using UnityEditor;
using UnityEngine;

namespace UnityNvim.Editor.Bridge
{
    /// <summary>Runs work queued from socket threads on Unity's main thread (EditorApplication.update keeps ticking in the background).</summary>
    public static class MainThreadQueue
    {
        static readonly ConcurrentQueue<Action> Queue = new ConcurrentQueue<Action>();
        static bool hooked;

        public static event Action Tick;

        public static void Enqueue(Action action)
        {
            Queue.Enqueue(action);
        }

        public static void Hook()
        {
            if (hooked)
                return;
            hooked = true;
            EditorApplication.update += Drain;
        }

        public static void Unhook()
        {
            hooked = false;
            EditorApplication.update -= Drain;
        }

        static void Drain()
        {
            while (Queue.TryDequeue(out var action))
            {
                try
                {
                    action();
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }
            Tick?.Invoke();
        }
    }
}
