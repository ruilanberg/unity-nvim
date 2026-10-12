using UnityEngine;

namespace Game
{
    public class Player : MonoBehaviour
    {
        [SerializeField] float speed = 5f;

        void Update()
        {
            transform.position += Vector3.right * speed * Time.deltaTime;
        }
    }
}
