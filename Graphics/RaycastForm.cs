using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using System.Windows.Input;

namespace GraphicsTest
{
    public partial class RaycastForm : Form
    {
        // radians = degress * PI/180
        private const int WORLD_BLOCK_SIZE = 10;

        private const double RADIANS_CONVERSION_FACTOR = Math.PI / 180.0;
        private const double PLAYER_START_X = 3.5 * WORLD_BLOCK_SIZE;
        private const double PLAYER_START_Y = 3.5 * WORLD_BLOCK_SIZE;
        private const int PLAYER_START_ANGLE_DEGREES = 90;
        private const int FIELD_OF_VIEW_DEGREES = 60;
        private const double PLAYER_STEP_AMOUNT = 2.0;
        private const int PLAYER_ROTATE_DEGREES_AMOUNT = 5;
        private const int KNOWN_COLOR_OFFSET = 30;
        private const int SCREEN_WIDTH = 300;
        private const int SCREEN_HEIGHT = 200;
        private const int SCREEN_CENTER_Y = SCREEN_HEIGHT / 2;
        private const int HALF_FIELD_OF_VIEW_DEGREES = FIELD_OF_VIEW_DEGREES / 2;
        private const int DRAW_LINE_WIDTH = 1;
        
        private const int WORLD_SIZE_X = 10;
        private const int WORLD_SIZE_Y = 10;

        //As we are drawing lines of 5 pixels wide, our angle increment is 1
        //If the lines where 1 pixel wide they would be 60/300 ie 0.2degrees
        private const double ANGLE_INCREMENT_DEGREES = ((double)FIELD_OF_VIEW_DEGREES / (double)SCREEN_WIDTH) * (double)DRAW_LINE_WIDTH;

        private double _playerX = PLAYER_START_X;
        private double _playerY = PLAYER_START_Y;
        private int _playerFacingDegrees = PLAYER_START_ANGLE_DEGREES;

        private Pen _floorPen = new Pen(new SolidBrush(Color.Gray), 1);
        private Pen _ceilingPen = new Pen(new SolidBrush(Color.Black), 1);

        private double[] _cosTable = new double[360];
        private double[] _sinTable = new double[360];

        // TODO precalculate COS and SIN Tables for 0...360 degrees in radians

        //Each block is 10 x 10, so there are 100x100 for the whole map
        //Player x,y of 15,15 is 1.5,1.5 in the world
        //To convert player co-ordinate to world co-ordinate, divide by 10
        //To convert world co-ordinate to player co-ordinate, multiply by 10
        int[,] world = new int[WORLD_SIZE_X, WORLD_SIZE_Y]
        {
            {1,9,1,9,1,9,1,9,1,9},
            {9,0,0,0,0,0,0,0,0,1},
            {9,0,0,0,0,0,0,0,0,9},
            {9,0,0,0,0,0,0,0,0,1},
            {9,0,0,0,0,0,0,0,0,9},
            {9,0,0,0,0,0,0,0,0,1},
            {9,0,0,0,0,0,0,0,0,9},
            {9,0,0,0,0,0,0,0,0,1},
            {9,0,0,0,0,0,0,0,0,9},
            {9,1,9,1,9,1,9,1,9,1},        
        };
                             
        public RaycastForm()
        {
            InitializeComponent();

            // Initialize lookup tables
            for (int i = 0; i < 360; i++)
            {
                _cosTable[i] = Math.Cos(i * RADIANS_CONVERSION_FACTOR);
                _sinTable[i] = Math.Sin(i * RADIANS_CONVERSION_FACTOR);
            }
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            DoubleBuffered = true;
            Application.Idle += Application_Idle;
        }

        private void Application_Idle(object sender, EventArgs e)
        {
            Invalidate();
        }

        /// <summary>
        /// Called everytime we invalidate the control
        /// Render the scene based on the current position
        /// </summary>
        /// <param name="e"></param>
        protected override void OnPaint(PaintEventArgs e)
        {
            ClearBackground(e.Graphics);
            Raycast(e.Graphics);
            DrawMap(e.Graphics);
            DrawInfo();
        }

        private void ClearBackground(Graphics g)
        {
            g.Clear(Color.White);
        }

        /// <summary>
        /// Draws a map at the top left corner of the screen
        /// </summary>
        /// <param name="g"></param>
        private void DrawMap(Graphics g)
        {
            // TODO draw a triangle to show FOV
            Bitmap map = new Bitmap(WORLD_SIZE_X, WORLD_SIZE_Y);
            Color pixelColor;

            for (int y = 0; y < WORLD_SIZE_Y; y++)
            {
                for (int x = 0; x < WORLD_SIZE_X; x++)
                {
                    //If floor, set to match floor color
                    if (world[y, x] == 0)
                    {
                        pixelColor = Color.Gray;
                    }
                    else
                    {
                        pixelColor = Color.FromKnownColor((KnownColor)world[y, x] + KNOWN_COLOR_OFFSET);
                    }
                    
                    map.SetPixel(x, y , pixelColor );
                }
            }

            //Mark the player position
            int xx = (int)(_playerX / WORLD_BLOCK_SIZE);
            int yy = (int)(_playerY / WORLD_BLOCK_SIZE);
            map.SetPixel(xx, yy, Color.Red);

            //Scale it up to 40 by 40 to make it easier to see
            g.DrawImage(map,new Rectangle(0, 0, 40, 40));
        }

        private void Raycast(Graphics g)
        {
            //As we loop from rayAngle to rayAngle plus FoV, we are "looking right"
            for (double rayAngleDegrees = _playerFacingDegrees; rayAngleDegrees <= _playerFacingDegrees + FIELD_OF_VIEW_DEGREES; rayAngleDegrees += ANGLE_INCREMENT_DEGREES)
            {
                double xIncrement = _cosTable[(int)(rayAngleDegrees % 360)] / 100; ////  Math.Cos((rayAngleDegrees % 360) * RADIANS_CONVERSION_FACTOR) / 100;
                double yIncrement = _sinTable[(int)(rayAngleDegrees % 360)] / 100; //// Math.Sin((rayAngleDegrees % 360) * RADIANS_CONVERSION_FACTOR) / 100;
                double testX = _playerX;
                double testY = _playerY;
                int rayLength = 1;
                int wallColour = 0;

                while (wallColour == 0)
                {
                    testX += xIncrement;
                    testY += yIncrement;
                    rayLength++;
                    int worldX = (int)(testX / WORLD_BLOCK_SIZE);
                    int worldY = (int)(testY / WORLD_BLOCK_SIZE);
                    wallColour = world[worldX, worldY];
                }

                //Set start x for the rectangles
                int x = (int)(((rayAngleDegrees - _playerFacingDegrees) * DRAW_LINE_WIDTH) / ANGLE_INCREMENT_DEGREES);

                //Compensate for fisheye view as ray is cast from center, so apply a reduction of 
                //half FoV to account for our main loop doing 0...60. Produces values -30 to 30
                double beta = rayAngleDegrees - _playerFacingDegrees - HALF_FIELD_OF_VIEW_DEGREES;
                // Cant use Cos Table here as using non integer range of values
                rayLength = (int)((double)rayLength * Math.Cos(beta * RADIANS_CONVERSION_FACTOR));

                //Scale the wall according to distance
                //If the rayLength is shorter, then the wall must be drawn bigger
                int wallHeight = (int)(((double)100000 /(double)rayLength) * 4);   
                if (wallHeight > SCREEN_HEIGHT)
                {
                    wallHeight = SCREEN_HEIGHT;
                }
                int halfWallHeight = wallHeight / 2;
                
                DrawCeiling(g, x, halfWallHeight);
                DrawWall(g, x, halfWallHeight, wallColour);
                DrawFloor(g, x, halfWallHeight);
            }
        }

        private void DrawCeiling(Graphics g, int x, int halfWallHeight)
        {
            Rectangle ceilingRect = new Rectangle(x, 0, DRAW_LINE_WIDTH, SCREEN_CENTER_Y - halfWallHeight);

            g.DrawRectangle(_ceilingPen, ceilingRect);
            g.FillRectangle(_ceilingPen.Brush, ceilingRect);
        }

        private void DrawWall(Graphics g, int x, int halfWallHeight, int wallColour)
        {
            Rectangle wallRect = new Rectangle(x, SCREEN_CENTER_Y - halfWallHeight, DRAW_LINE_WIDTH, halfWallHeight * 2);

            SolidBrush wallBrush = new SolidBrush(Color.FromKnownColor((KnownColor)wallColour + KNOWN_COLOR_OFFSET));
            Pen wallPen = new Pen(wallBrush);

            g.DrawRectangle(wallPen, wallRect);
            g.FillRectangle(wallBrush, wallRect);
        }

        private void DrawFloor(Graphics g, int x, int halfWallHeight)
        {
            Rectangle floorRect = new Rectangle(x, SCREEN_CENTER_Y + halfWallHeight, DRAW_LINE_WIDTH, SCREEN_CENTER_Y - halfWallHeight);

            g.DrawRectangle(_floorPen, floorRect);
            g.FillRectangle(_floorPen.Brush, floorRect);
        }

        private void DrawInfo()
        {
            infoLabel.Text = string.Format("Player X: {0} Y: {1} World X: {2} Y: {3} Facing: {4}",
                _playerX.ToString("#0.00"),
                _playerY.ToString("#0.00"),
                (int)(_playerX / WORLD_BLOCK_SIZE),
                (int)(_playerY / WORLD_BLOCK_SIZE),
                _playerFacingDegrees.ToString());
        }

        /// <summary>
        /// Move the player
        /// See https://permadi.com/1996/05/ray-casting-tutorial-15/
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Form1_KeyDown(object sender, KeyEventArgs e)
        {
            switch (e.KeyCode)
            {
                case Keys.D:
                case Keys.Right:
                    {
                        _playerFacingDegrees = (_playerFacingDegrees + PLAYER_ROTATE_DEGREES_AMOUNT) % 360;
                        break;
                    }
                case Keys.A:
                case Keys.Left:
                    {
                        _playerFacingDegrees = (_playerFacingDegrees + (360 - PLAYER_ROTATE_DEGREES_AMOUNT)) % 360;
                        break;
                    }
                case Keys.W:
                case Keys.Up:
                    {
                        double newPlayerX = _playerX + (_cosTable[_playerFacingDegrees] * PLAYER_STEP_AMOUNT);
                        double newPlayerY = _playerY + (_sinTable[_playerFacingDegrees] * PLAYER_STEP_AMOUNT);
                        TryUpdatePlayerPosition(newPlayerX, newPlayerY);
                        break;
                    }
                case Keys.S:
                case Keys.Down:
                    {
                        double newPlayerX = _playerX - (_cosTable[_playerFacingDegrees] * PLAYER_STEP_AMOUNT);
                        double newPlayerY = _playerY - (_sinTable[_playerFacingDegrees] * PLAYER_STEP_AMOUNT);
                        TryUpdatePlayerPosition(newPlayerX, newPlayerY);
                        break;
                    }
                case Keys.R:
                    {
                        //Reset to start positions and angle
                        _playerFacingDegrees = PLAYER_START_ANGLE_DEGREES;
                        _playerX = PLAYER_START_X;
                        _playerY = PLAYER_START_Y;
                        break;
                    }
                default:
                    {
                        break;
                    }
            }
        }

        /// <summary>
        /// Check new position is not in a wall, if so then update player position
        /// </summary>
        /// <param name="x"></param>
        /// <param name="y"></param>
        private void TryUpdatePlayerPosition(double x, double y)
        {
            if (world[(int)(x / WORLD_BLOCK_SIZE), (int)(y / WORLD_BLOCK_SIZE)] == 0)
            {
                _playerX = x;
                _playerY = y;
            }
        }
    }
}
