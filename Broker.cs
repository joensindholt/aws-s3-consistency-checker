using System;
using System.Collections.Generic;

namespace AwsS3ConsistencyChecker
{
    public class Broker
    {
        private Dictionary<string, List<Action<int>>> _subscribers = new Dictionary<string, List<Action<int>>>();

        public Broker()
        {
        }

        public void Notify(string message, int identifier = -1)
        {
            if (_subscribers.ContainsKey(message))
            {
                _subscribers[message].ForEach(action => action.Invoke(identifier));
            }
        }

        public void Subscribe(string message, Action<int> action)
        {
            if (!_subscribers.ContainsKey(message))
            {
                _subscribers[message] = new List<Action<int>>();
            }

            _subscribers[message].Add(action);
        }
    }
}