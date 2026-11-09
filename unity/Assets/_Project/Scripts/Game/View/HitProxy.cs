using UnityEngine;

namespace TrainSurvivor.Game.View
{
    /// <summary>
    /// Ballistics-only collision proxy for one enemy or blockade. It carries the sim id that a
    /// hit reports, so a projectile sweep never searches the scene and never guesses which
    /// entity a collider belongs to. The owning view binds it to the pooled body every frame
    /// and disables it the moment that body is freed or the target dies, so a dead enemy has
    /// no hittable corpse.
    /// </summary>
    public sealed class HitProxy : MonoBehaviour
    {
        public int EntityId { get; private set; } = -1;
        public bool IsBlockade { get; private set; }

        CapsuleCollider _capsule;
        BoxCollider _box;

        public static HitProxy CreateCapsule(Transform parent, string name, float radius, float height)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var proxy = go.AddComponent<HitProxy>();
            var c = go.AddComponent<CapsuleCollider>();
            c.radius = radius;
            c.height = height;
            c.direction = 1;          // Y axis: every enemy stands upright
            c.isTrigger = true;       // collision queries only; never pushes physics bodies
            proxy._capsule = c;
            proxy.Disable();
            return proxy;
        }

        public static HitProxy CreateBox(Transform parent, string name, Vector3 size)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var proxy = go.AddComponent<HitProxy>();
            var b = go.AddComponent<BoxCollider>();
            b.size = size;
            b.isTrigger = true;
            proxy._box = b;
            proxy.Disable();
            return proxy;
        }

        /// <summary>Point this proxy at an entity. Cheap to call every frame: a no-op when the
        /// body is already bound to the same id.</summary>
        public void Bind(int entityId, bool isBlockade)
        {
            if (entityId == EntityId && isBlockade == IsBlockade) return;
            EntityId = entityId;
            IsBlockade = isBlockade;
            SetColliderEnabled(entityId > 0);
        }

        public void Move(Vector3 worldPosition) => transform.position = worldPosition;

        public void SetBoxSize(Vector3 size)
        {
            if (_box != null) _box.size = size;
        }

        public void SetCapsuleSize(float radius, float height)
        {
            if (_capsule == null) return;
            _capsule.radius = radius;
            _capsule.height = Mathf.Max(height, radius * 2f);
        }

        public void Disable()
        {
            EntityId = -1;
            IsBlockade = false;
            SetColliderEnabled(false);
        }

        void SetColliderEnabled(bool on)
        {
            if (_capsule != null) _capsule.enabled = on;
            if (_box != null) _box.enabled = on;
        }

        /// <summary>The proxy a sweep hit, if the collider belongs to one.</summary>
        public static HitProxy Of(Collider collider)
        {
            if (collider == null) return null;
            return collider.GetComponentInParent<HitProxy>();
        }
    }
}
