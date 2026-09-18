using UnityEngine;
using UnityEngine.InputSystem;

public class GameManager : MonoBehaviour
{
    [Header("Grid Settings")]
    public int width = 60;          // celdas a lo ancho
    public int height = 40;         // celdas a lo alto
    public float updateTime = 0.1f; // segundos entre una generacion y la siguiente

    private bool[,] grid;           // estado actual (true = viva)
    private bool[,] nextGrid;       // doble buffer: aqui se escribe la generacion siguiente
    private float timer;            // acumula tiempo hasta completar un updateTime
    private bool isPaused = false;
    private Texture2D texture;      // toda la grilla se dibuja en una sola textura
    private Color32[] pixels;       // buffer de color: 1 celda = 1 pixel

    private static readonly Color32 AliveColor = new Color32(0, 0, 0, 255);
    private static readonly Color32 DeadColor = new Color32(255, 255, 255, 255);

    void Start()
    {
        // Los arrays se reservan una sola vez, nunca dentro del bucle.
        grid = new bool[width, height];
        nextGrid = new bool[width, height];

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

    // Mata todas las celdas.
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
                grid[x, y] = Random.value > 0.90f; // ~10% vivas
            }
        }
        UpdateVisuals();
    }

    // El corazon del automata: calcula una generacion entera.
    void Step()
    {
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                int aliveNeighbors = CountAliveNeighbors(x, y);
                bool alive = grid[x, y];

                if (alive && (aliveNeighbors < 2 || aliveNeighbors > 3))
                    nextGrid[x, y] = false; // Muere: soledad (<2) o sobrepoblacion (>3)
                else if (!alive && aliveNeighbors == 3)
                    nextGrid[x, y] = true;  // Nace: exactamente 3 vecinos
                else
                    nextGrid[x, y] = alive; // Se mantiene
            }
        }

        // Leemos de grid y escribimos en nextGrid: nunca pisamos lo que falta por leer, asi que el orden del recorrido da igual.
        var temp = grid;
        grid = nextGrid;
        nextGrid = temp;
    }

    // Vecindad de Moore: las 8 celdas que rodean a (x, y).
    int CountAliveNeighbors(int x, int y)
    {
        int count = 0;

        for (int dx = -1; dx <= 1; dx++)
        {
            for (int dy = -1; dy <= 1; dy++)
            {
                if (dx == 0 && dy == 0) continue; // no me cuento a mi mismo
                int nx = x + dx;
                int ny = y + dy;

                // Fuera de la grilla cuenta como muerto
                if (nx >= 0 && nx < width && ny >= 0 && ny < height)
                {
                    if (grid[nx, ny]) count++;
                }
            }
        }

        return count;
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
                pixels[row + x] = grid[x, y] ? AliveColor : DeadColor;
            }
        }

        texture.SetPixels32(pixels);
        texture.Apply();  // una sola subida a la GPU por generacion
    }
}
