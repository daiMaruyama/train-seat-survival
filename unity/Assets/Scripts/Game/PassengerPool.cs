using System.Collections.Generic;
using UnityEngine;

namespace TrainSurvival.Game
{
    /// <summary>
    /// 乗客の体を使い回すプール。乗客は駅ごとに入れ替わるので、生成／破棄を繰り返す代わりにここで
    /// 再利用する（動かない車内の方にプールは不要で、動く乗客にこそ効く）。見た目は
    /// Resources/Passengers のローポリ人型プレハブからランダムに選び、取り出すたびに身長も
    /// 少し変えて（<see cref="PassengerActor.RandomizeLook"/>）群衆に見せる。
    /// </summary>
    public sealed class PassengerPool
    {
        private static GameObject[] _variants;

        private readonly Transform _parent;
        private readonly Queue<PassengerActor> _idle = new Queue<PassengerActor>();

        public PassengerPool(Transform parent)
        {
            _parent = parent;
            if (_variants == null || _variants.Length == 0)
            {
                _variants = Resources.LoadAll<GameObject>("Passengers");
                if (_variants.Length == 0)
                {
                    Debug.LogWarning("[PassengerPool] Resources/Passengers にプレハブが無いためカプセルで代用");
                }
            }
        }

        public PassengerActor Get()
        {
            PassengerActor actor = _idle.Count > 0 ? _idle.Dequeue() : Create();
            actor.gameObject.SetActive(true);
            // 着せ替え（RandomizeLook）は Director がチューニング値を注入した後に呼ぶ
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
            GameObject variant = _variants.Length > 0
                ? _variants[Random.Range(0, _variants.Length)]
                : null;
            actor.BuildBody(variant);
            return actor;
        }
    }
}
