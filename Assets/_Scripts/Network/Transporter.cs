using UnityEngine;

public class Transporter : MonoBehaviour
{
    public Transporter Instance;

    #region Singleton
    private void Awake()
    {
        DontDestroyOnLoad(this);
        Instance = this;
    }
    #endregion
}
