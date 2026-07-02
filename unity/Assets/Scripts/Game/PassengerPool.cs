using System.Collections.Generic;
using UnityEngine;

namespace TrainSurvival.Game
{
    /// <summary>
    /// 乗客の体を使い回すプール。乗客は駅ごとに入れ替わるので、生成／破棄を繰り返す代わりにここで
    /// 再利用する（動かない車内の方にプールは不要で、動く乗客にこそ効く）。取り出すたびに着せ替え
    /// （<see cref="PassengerActor.RandomizeLook"/>）するので、同じ体でも別人に見える。
    /// </summary>
    public sealed class PassengerPool
    {
        private readonly Transform _parent;
        private readonly Queue<PassengerActor> _idle = new Queue<PassengerActor>();

        public PassengerPool(Transform parent)
        {
            _parent = parent;
        }

        public PassengerActor Get()
        {
            PassengerActor actor = _idle.Count > 0 ? _idle.Dequeue() : Create();
            actor.gameObject.SetActive(true);
            actor.RandomizeLook();
            return actor;
        }

        public void Return(PassengerActor actor)
        {
            if (actor == null)
            {
                return;
            }
            actor.gameObject.SetActive(false);
            _idle.Enqueue(actor);
        }

        private PassengerActor Create()
        {
            var go = new GameObject("Passenger");
            go.transform.SetParent(_parent, false);
            var actor = go.AddComponent<PassengerActor>();
            actor.BuildBody();
            return actor;
        }
    }
}
