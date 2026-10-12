using UnityEngine;

public class Hello : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod]
    static void OnPlay() => Debug.Log("hello from Assembly-CSharp");
}
