using UnityEngine;
using UnityEngine.InputSystem;

public class GameManager : MonoBehaviour
{
    [Header("Grid Settings")]
    public int width = 60;          // celdas a lo ancho
    public int height = 40;         // celdas a lo alto
    public float updateTime = 0.1f; // segundos entre una generacion y la siguiente

    private bool[,] grid;           // estado actual (true = viva)
    //private bool[,] nextGrid;       // doble buffer: aqui se escribe la generacion siguiente
    private float timer;            // acumula tiempo hasta completar un updateTime
    private bool isPaused = false;
    private Texture2D texture;      // toda la grilla se dibuja en una sola textura
    private Color32[] pixels;       // buffer de color: 1 celda = 1 pixel

    private static readonly Color32 SandColor = new Color32(0, 0, 0, 255);
    private static readonly Color32 EmpyColor = new Color32(255, 255, 255, 255);

    void Start()
    {
        // Los arrays se reservan una sola vez, nunca dentro del bucle.
        grid = new bool[width, height];
        //nextGrid = new bool[width, height]; --> ya no lo necesito

        // Este script no lee teclas: escucha los eventos del InputManager.
        InputManager.Instance.OnPause += TogglePause;
        InputManager.Instance.OnRestart += RestartSimulation;
        InputManager.Instance.OnClear += ClearSimulation;
        InputManager.Instance.OnToggleCell += ToggleCellInput;

        BuildTexture();
        RandomizeGrid();
    }

    void Update()
    {
        if (isPaused) return;

        timer += Time.deltaTime;
        if (timer >= updateTime)
        {
            Step();          // 1) calcular la nueva generacion
            UpdateVisuals(); // 2) dibujarla
            timer = 0f;
        }
    }

    // Tecla P. Congela el avance
    void TogglePause()
    {
        isPaused = !isPaused;
        Debug.Log(isPaused ? "Simulaci�n pausada" : "Simulaci�n reanudada");
    }

    // Clic izquierdo / boton A. Enciende o apaga una celda a mano.
    void ToggleCellInput()
    {
        // Con mouse (PC): la celda que este bajo el cursor.
        if (Mouse.current != null)
        {
            HandleMouseClick();
            return;
        }

        // Sin mouse (gamepad): la celda del centro de la camara.
        Vector3 camPos = Camera.main.transform.position;
        ToggleCellAtWorld(camPos);
    }


    // Tecla E. Deja el tablero vacio para dibujar desde cero.
    void ClearSimulation()
    {
        Debug.Log("Limpiando simulaci�n...");
        ClearGrid();
        timer = 0f;
    }

    // Tecla R. Vuelve a sembrar una poblacion aleatoria.
    void RestartSimulation()
    {
        Debug.Log("Reiniciando simulaci�n...");
        RandomizeGrid();
        timer = 0f;
    }

    // Un unico sprite para toda la grilla.
    void BuildTexture()
    {
        texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
        texture.filterMode = FilterMode.Point;    // pixeles nitidos, sin difuminar
        texture.wrapMode = TextureWrapMode.Clamp;

        pixels = new Color32[width * height];

        // pixelsPerUnit = 1 y pivote (0,0): el pixel (x,y) ocupa [x, x+1] x [y, y+1].
        Sprite sprite = Sprite.Create(
            texture,
            new Rect(0, 0, width, height),
            Vector2.zero,
            1f);

        SpriteRenderer rend = GetComponent<SpriteRenderer>();
        if (rend == null) rend = gameObject.AddComponent<SpriteRenderer>();

        rend.sprite = sprite;
    }

    // Vacia todas las celdas.
    public void ClearGrid()
    {
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                grid[x, y] = false;
            }
        }
        UpdateVisuals();
    }

    // Siembra al azar.
    void RandomizeGrid()
    {
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                grid[x, y] = Random.value > 0.90f; // ~10% de celdas llenas con arena
            }
        }
        UpdateVisuals();
    }

    // El corazon del automata: calcula una generacion entera.
    void Step()
    {
        // Se recorre de abajo hacia arriba para verificar si las celdas estan libres o no, para que el grano de arena caiga o no.
        for (int y = 1; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                if (grid[x, y])
                {
                    MoveSandCell(x, y);
                }
            }
        }
    }

    // Vecindad de Moore: las 8 celdas que rodean a (x, y).
    void MoveSandCell(int x, int y)
    {
       // Arena cayendo, si abajo esta libre, cae directo
       if (IsFree(x, y - 1))
       {
        grid[x, y] = false;
        grid[x, y - 1] = true;
        return;
       }

       // Colision con otra part de arena, si abajo esta acupado, se va en diagonal
       bool leftFree = IsFree(x - 1, y - 1);
       bool rightFree = IsFree(x + 1, y - 1);

       // Se hace el movimiento en diagonal
       if (leftFree && rightFree)
       {
        // ambas diagonales libres, elige alazar
        int dx = (Random.value < 0.5f) ? -1 : 1;
        grid[x, y] = false;
        grid[x + dx, y - 1] = true;
       }
       else if (leftFree)
       {
        // caso abajo-izquierda libre
        grid[x, y] = false;
        grid[x - 1, y - 1] = true;
       }
       else if (rightFree)
       {
        // caso abajo-derecha libre
        grid[x, y] = false;
        grid[x + 1, y - 1] = true;
       }
       // al final se bloquea si ambas estan ocupadas, apilandose una por una
    }

    // Para que la celula no siga derecho y se salga del tablero
    bool IsFree(int x, int y)
    {
        if (x < 0 || x >= width) return false;
        if (y < 0 || y >= height) return false;
        return !grid[x, y];
    }

    void HandleMouseClick()
    {
        // Pixeles de pantalla -> unidades de mundo.
        Vector3 worldPos = Camera.main.ScreenToWorldPoint(Mouse.current.position.ReadValue());
        ToggleCellAtWorld(worldPos);
    }

    // Unidades de mundo -> indices de la grilla.
    void ToggleCellAtWorld(Vector3 worldPos)
    {
        // Floor y no Round: el pixel (x,y) cubre el rango [x, x+1).
        int x = Mathf.FloorToInt(worldPos.x);
        int y = Mathf.FloorToInt(worldPos.y);

        if (x < 0 || x >= width || y < 0 || y >= height) return; // clic fuera del tablero

        grid[x, y] = !grid[x, y];
        UpdateVisuals();
    }

    // Vuelca la grilla de bool a colores y la sube a la GPU de un golpe.
    void UpdateVisuals()
    {
        // La fila 0 de la textura es la de abajo, igual que en la grilla.
        for (int y = 0; y < height; y++)
        {
            int row = y * width;
            for (int x = 0; x < width; x++)
            {
                pixels[row + x] = grid[x, y] ? SandColor : EmpyColor;
            }
        }

        texture.SetPixels32(pixels);
        texture.Apply();  // una sola subida a la GPU por generacion
    }
}
