using System.Collections.Generic;
using UnityEngine;

namespace TrainSurvival.Game
{
    /// <summary>
    /// 乗客のカプセルを、乗り降りのたびに生成／破棄せず使い回す。乗客は駅ごとに入れ替わるので、まさに
    /// プールが効く場所（動かない車内の方は不要）。カプセルは細くして、隣と重ならず1席に1人収まるように
    /// してある。今はグレーボックス、後でキャラクター prefab に差し替える。
    /// </summary>
    public sealed class PassengerPool
    {
        private static readonly Color BodyColor = new Color(0.72f, 0.72f, 0.75f);

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
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            go.name = "Passenger";
            go.transform.SetParent(_parent, false);
            go.transform.localScale = new Vector3(0.42f, 0.6f, 0.42f);
            Object.Destroy(go.GetComponent<Collider>());

            var renderer = go.GetComponent<Renderer>();
            Material material = renderer.material;
            material.color = BodyColor;
            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", BodyColor);
            }

            return go.AddComponent<PassengerActor>();
        }
    }
}
