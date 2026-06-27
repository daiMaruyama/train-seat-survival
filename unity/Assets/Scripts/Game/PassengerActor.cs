using System;
using System.Collections.Generic;
using UnityEngine;

namespace TrainSurvival.Game
{
    /// <summary>
    /// 乗客の体。席に即座にスナップさせることも、短い経路（降車なら 席→通路→ドア、乗車なら
    /// ドア→通路→席）を歩かせることもでき、到着時にコールバックを呼ぶ（例：プールへ戻す／席に落ち着く）。
    /// </summary>
    public sealed class PassengerActor : MonoBehaviour
    {
        [SerializeField] private float _speed = 2.6f;

        private readonly Queue<Vector3> _path = new Queue<Vector3>();
        private Action _onArrive;
        private bool _moving;

        public void Snap(Vector3 position, Quaternion rotation)
        {
            _path.Clear();
            _onArrive = null;
            _moving = false;
            transform.SetPositionAndRotation(position, rotation);
        }

        public void Travel(IReadOnlyList<Vector3> waypoints, Action onArrive)
        {
            _path.Clear();
            foreach (Vector3 w in waypoints)
            {
                _path.Enqueue(w);
            }
            _onArrive = onArrive;
            _moving = _path.Count > 0;
        }

        private void Update()
        {
            if (!_moving)
            {
                return;
            }

            Vector3 target = _path.Peek();
            Vector3 to = target - transform.position;
            float distance = to.magnitude;
            float step = _speed * Time.deltaTime;

            if (distance <= step)
            {
                transform.position = target;
                _path.Dequeue();
                if (_path.Count == 0)
                {
                    _moving = false;
                    Action callback = _onArrive;
                    _onArrive = null;
                    callback?.Invoke();
                }
                return;
            }

            transform.position += to / distance * step;
            Vector3 flat = new Vector3(to.x, 0f, to.z);
            if (flat.sqrMagnitude > 0.0001f)
            {
                transform.rotation = Quaternion.LookRotation(flat);
            }
        }
    }
}
