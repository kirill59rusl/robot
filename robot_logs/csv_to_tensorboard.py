import os
import pandas as pd
from torch.utils.tensorboard import SummaryWriter
# Примечание: Если вы хотите использовать чистый TensorFlow вместо PyTorch,
# можно использовать tf.summary, но SummaryWriter из torch — самый простой и легкий способ.

# Путь к вашему CSV файлу
csv_file_path = "diagnostic_log.csv"  # Замените на имя вашего файла
log_dir = "./tb_logs"            # Папка, куда сохранятся логи для TensorBoard

if not os.path.exists(csv_file_path):
    print(f"Ошибка: Файл {csv_file_path} не найден!")
    exit()

# Читаем CSV файл
# UTF-8-BOM (сигнатура '\ufeff' в начале файла) обрабатывается автоматически при encoding='utf-8-sig'
df = pd.read_csv(csv_file_path, encoding='utf-8-sig')

# Инициализируем TensorBoard Writer
writer = SummaryWriter(log_dir)

print(f"Начало конвертации {len(df)} строк...")

# Проверяем, какие колонки будут выступать в качестве осей
# В вашем логе есть 'step' — будем использовать его как глобальный шаг (global_step)
step_col = 'step' if 'step' in df.columns else None
time_col = 'time' if 'time' in df.columns else None

# Колонки, которые мы НЕ хотим строить как графики метрик
ignored_cols = {'step', 'time', 'epId'}

for index, row in df.iterrows():
    # Определяем текущий шаг
    current_step = int(row[step_col]) if step_col else index
    
    # Также запишем время, если оно есть
    current_time = row[time_col] if time_col else None
    
    for col in df.columns:
        if col in ignored_cols:
            continue
        
        # Получаем значение метрики
        val = row[col]
        
        # Записываем в TensorBoard
        # Разделим по группам для удобства отображения в интерфейсе (например, rewards, sensors, actuators)
        if 'Rew' in col:
            tag = f"Rewards/{col}"
        elif col in ['uz', 'irL', 'irR', 'gripIR', 'ballSeen', 'ballAngle', 'perceivedDist', 'trueDist']:
            tag = f"Sensors/{col}"
        elif col in ['gas', 'steering', 'camYaw', 'hasBall', 'holdTicks', 'isRetrying']:
            tag = f"Actuators_State/{col}"
        else:
            tag = f"Ego/{col}"
            
        writer.add_scalar(tag, val, global_step=current_step, walltime=current_time)

writer.close()
print(f"Успешно! Логи сохранены в папку: {log_dir}")
print("Теперь вы можете запустить TensorBoard.")