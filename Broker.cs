using System;
using System.Collections.Generic;

namespace AwsS3ConsistencyChecker
{
    public class Broker
    {
        private Dictionary<string, Action<int>> _subscribers = new Dictionary<string, Action<int>>();

        public Broker()
        {
        }

        public void Notify(string message, int identifier = -1)
        {
            if (_subscribers.ContainsKey(message))
            {
                _subscribers[message].Invoke(identifier);
            }
        }

        public void Subscribe(string message, Action<int> action)
        {
            _subscribers[message] = action;
        }
    }
}